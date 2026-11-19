using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // 第一批 A1/A2 经营恢复规则验收（F1 独立规则线）。
    // 范围：基础油盐软缺件与旧 v2 计费迁移（root-review 接受标准）、六菜软硬门槛、
    // 批量按份守恒、保存边界、推荐兼容/补齐/能源（A2 未交付处以 required-pending
    // 显式挂起）、正常/零营业日账守恒。全部用真实生产对象与真实交易路径
    // （RunState.Act 收银 / BusinessSession.BeginOrder·Action·Serve /
    // CampaignState.CommitDay），不直接改 cash 充数，不复制生产算法自证，
    // 不反射检查字段存在性。旧档用例使用旧算法真实账义夹具（旧 BeginOrder 已
    // 整批扣油盐的库存数 + 完全不含新字段的 JSON），不用“新档删字段”冒充。
    // 每个用例都是隔离夹具（fixture=true），不声称人工正常开局成功。
    public static class Batch1RecoveryValidation
    {
        [Serializable] public sealed class CheckResult
        {
            public string id,area,expect,actual,detail;public bool passed;
        }
        [Serializable] public sealed class PendingCheck
        {
            public string id,area,kind,reason; // kind: required=关键规则未完成； external=待实机/UI
        }
        [Serializable] public sealed class Report
        {
            public bool success;          // 本次已执行 checks 全过且无运行时错误（不代表整批完成）
            public bool complete;         // success 且无任何未执行的跨套件/UI 项
            public int checksPassed;
            public bool fixture=true;
            public string scope="Batch1 A1/A2 business-recovery rule validation; isolated model fixtures via real production objects; not a full two-day playthrough";
            public CheckResult[] checks=Array.Empty<CheckResult>();
            public string[] failed=Array.Empty<string>();
            public string[] errors=Array.Empty<string>();
            public PendingCheck[] pending=Array.Empty<PendingCheck>();
        }
        static readonly List<CheckResult> checks=new List<CheckResult>();
        static readonly List<string> errors=new List<string>();
        static readonly List<PendingCheck> pending=new List<PendingCheck>();
        static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static GameObject fixtureRoot;static GameFlow mapper;static string output;

        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--batch1-recovery-output");
            output=Path.GetFullPath(at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.dataPath,"../../art-v14/claude-batch-1/F1/batch1-recovery.json"));
            checks.Clear();errors.Clear();pending.Clear();
            Application.logMessageReceived+=CaptureError;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                fixtureRoot=new GameObject("Batch1RecoveryFixture");
                var map=new GameObject("ProductionSetupMapper");map.transform.SetParent(fixtureRoot.transform,false);map.SetActive(false);
                mapper=map.AddComponent<GameFlow>();
                // ---- A1 软缺件运行规则 ----
                Case("A1.soft-gates","A1 软硬门槛",
                    "seasoning=0 时六道菜（含无seasoning配方的预制饭/冷饮）均可选且硬门槛仍拦主料/锅具/工具/能源/包材；SoftMissing 给出非空警示；生产常量味效-10/诚信-10",
                    SoftGates);
                Case("A1.soup20-seasoning0","A1 油盐0×20份汤锅",
                    "batch=20 不被1份油盐卡住；Serve 每份独立判定：seasoning 恒0不扣负、20份全部记缺调味、IntegrityChange=-200、基础价不变、账本守恒",
                    SoupTwentySeasoningZero);
                Case("A1.soup20-seasoning1","A1 油盐1×20份汤锅",
                    "第1份消耗唯一1份油盐无惩罚；第2..20份每份-10诚信；剩1份不让整锅免费正常；夹具补足后新份不继承缺料",
                    SoupTwentySeasoningOne);
                Case("A1.save-boundaries","A1 保存边界",
                    "投料前/Prep/已调味/剩熟食/已成交五边界 CaptureSave→JSON→Restore 后金额/份数/缺调味事实完全一致，恢复不重复扣",
                    SaveBoundaries);
                Case("A1.no-false-penalty","A1 无错误惩罚",
                    "油盐充足时 Serve 扣1份油盐、IntegrityChange=0、Unseasoned=0",
                    NoFalsePenalty);
                // ---- A1 旧 v2 计费迁移（root-review 接受标准；旧算法账义夹具）----
                Case("A1.migrate-full-pot","A1 旧档迁移·整锅已预付",
                    "旧v2汤锅20份已在投料扣20油盐、旧库存油盐0：Restore 迁移后锅内剩余批份建为全额抵扣并逐份抵至0；售出不得二次扣料、不得按缺料处罚；原本硬条件与成交金额不变",
                    MigrateFullPot);
                Case("A1.migrate-partial-sold","A1 旧档迁移·部分售出",
                    "旧v2已成交5份、锅内剩15份、旧库存油盐=初始−20：恢复后剩余批次全额抵扣售完，不扣库存不处罚，已成交事实原样（不退钱不退料）",
                    MigratePartialSold);
                Case("A1.migrate-resave-idempotent","A1 旧档迁移·多次存读",
                    "迁移后写盘为v3；同档反复 CaptureSave→Restore 不重复抵扣，prepaid 随 Serve 只减不增至0",
                    MigrateResaveIdempotent);
                Case("A1.migrate-prepared-cold-no-credit","A1 旧档迁移·预制饭/冷饮不虚构抵扣",
                    "旧配方无seasoning的预制饭与冷饮：不建 prepaidSeasoning；库存0时新成交按新规则记缺料处罚、库存有则正常每份扣1",
                    MigratePreparedColdNoCredit);
                Case("A1.migrate-idle-dirty-no-credit","A1 旧档迁移·Idle/Dirty无credit+非法credit拒绝",
                    "Idle/Dirty/报废批次不建 credit（Dirty 需旧报废台账守恒）；prepaid>batchRemaining 或负数的新档被拒绝",
                    MigrateIdleDirtyAndInvalidCredit);
                Case("A1.new-batch-no-inherit","A1 新批次不继承抵扣",
                    "迁移批次售完后夹具补料开新锅：prepaidSeasoning=0，按新规则每份扣库存",
                    NewBatchNoInherit);
                // ---- A2 可跑部分（当前生产 API）----
                Case("A2.recommend-compatible","A2 推荐兼容(B03留家/V02在车)",
                    "推荐不得生成 B01+V02 非法组合；经真实采购+BuildBusinessSetup 后目标菜单 Missing 为空；不无偿搬动家中B03",
                    RecommendCompatible);
                Case("A2.basket-no-pay","A2 补齐不付款",
                    "AddSupply 只入篮：CashCents/Supplies 不变；重复补齐不复制；满篮拒绝且现金库存仍不变",
                    BasketNoPay);
                Case("A2.energy-refill","A2 能源补齐",
                    "E01已装+煤0 推荐含E01且AddSupply允许；煤>0拒绝重复购；B02卡式气0不被E03假供能；煤0/煤足真实缺料判定",
                    EnergyRefill);
                Case("A2.recommend-gap","A2 推荐真实缺口与总价",
                    "货架原料计入总价；补工具后真实付款装车可开摊；满篮提示空间缺口且实际购买拒绝，不改钱物",
                    RecommendGaps);
                // ---- A7/A3 日账守恒 ----
                Case("A7.zero-day-ledger","A7 零营业日账",
                    "真实付款采购、有可售菜但不开摊直接Close：revenue=0、closing=opening−purchases（ tank 满45 无夜间费）、CommitDay幂等、次日现金/库存一致",
                    ZeroDayLedger);
                Case("A3.sales-day-ledger","A3 正常营业日账",
                    "缺调味成交3份汤：campaign.cash=opening−purchases+revenue−nightFuel、ledger.sales合计=revenue、单价保持基础价、成交只入账一次",
                    SalesDayLedger);
                // ---- 挂起项：关键规则 pending 使 complete=false ----
                Pending("A6.sell-entry","A2 经济恢复·旧货代收","external",
                    "关键模型规则由 Guochao.Editor.Batch1ResaleValidation.Run 独立验证，必须同时查看 batch1-resale 输出，F1 不代表该套已通过。界面确认/取消、交接耗时与键鼠空间操作仍需实机验收。");
                Pending("A2.paid-unloaded-load-fail","A2 已付款未装车守恒","external",
                    "交叉引用 Batch1ResaleValidation 的旧已付货超时、带回家与重读用例；F1 不合并或代称该套结果。");
                Pending("A1.integrity-ledger","A1 诚信跨日","external",
                    "交叉引用 Batch1ResaleValidation 的跨日诚信只入账一次与 CampaignSaveStore 存读用例。");
                Pending("A1.zero-business-ui","A1 零营业出口 UI","external",
                    "模型层 Close+CommitDay 零营收链已由 A7.zero-day-ledger 覆盖；BusinessHUD 独立'今天不开摊'按钮与缺油盐警示样式待实机/UI 截图确认（F 人工键鼠项）。");
            }
            catch(Exception e){checks.Add(new CheckResult{id="fixture",area="setup",passed=false,expect="fixtures constructible",actual="EXCEPTION",detail=e.ToString()});}
            finally
            {
                if(fixtureRoot!=null)UnityEngine.Object.DestroyImmediate(fixtureRoot);fixtureRoot=null;mapper=null;
                Application.logMessageReceived-=CaptureError;
            }
            bool success=checks.All(x=>x.passed)&&errors.Count==0;
            // External suites/UI have not executed in this process: never turn
            // a cross-reference into a claim that the whole batch is complete.
            bool complete=success&&pending.Count==0;
            var report=new Report{success=success,complete=complete,checksPassed=checks.Count(x=>x.passed),
                checks=checks.ToArray(),failed=checks.Where(x=>!x.passed).Select(x=>x.id).ToArray(),errors=errors.ToArray(),pending=pending.ToArray()};
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log((success?"BATCH1_RECOVERY_CHECKS_PASS ":"BATCH1_RECOVERY_CHECKS_FAIL ")+output+(complete?"":" INCOMPLETE required-pending="+pending.Count(x=>x.kind=="required")));
            if(Application.isBatchMode)EditorApplication.Exit(success?0:1);
        }
        static string NoFalsePenalty()
        {
            var s=SoupFixture(seasoning:5);
            Need(s.SoftMissing(3)==""&&s.SeasoningAvailable,"unexpected shortage warning");
            Need(s.Open(),"open refused");ServeOne(s,0,"P07");
            Need(s.Count("seasoning")==4,"full-stock serve must debit exactly 1 portion");
            Need(s.IntegrityChange==0&&s.UnseasonedPortionsServed==0,"false penalty at full stock");
            Need(s.CashCents==51000,"full-stock sale not received once");
            s.Close();return "seasoning 5->4; no shortage penalty; received 1000 cents once";
        }
        static void CaptureError(string message,string stack,LogType type)
        {if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void Pending(string id,string area,string kind,string reason){pending.Add(new PendingCheck{id=id,area=area,kind=kind,reason=reason});}
        static void Case(string id,string area,string expect,Func<string> body)
        {
            try{string detail=body();checks.Add(new CheckResult{id=id,area=area,expect=expect,actual="verified",passed=true,detail=detail});}
            catch(Exception e){checks.Add(new CheckResult{id=id,area=area,expect=expect,actual="FAILED",passed=false,detail=e.Message});}
        }
        static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void NeedReject(Action action,string message){try{action();}catch(ArgumentException){return;}throw new InvalidOperationException(message);}
        static RunProfile Profile(string career,string[] traits=null)
        {var p=new RunProfile{careerId=career,selectedTraits=traits??Array.Empty<string>()};if(traits==null&&p.Balance<0)p.selectedTraits=new[]{"迟钝学者"};Need(p.Validate(out string reason),reason);return p;}
        static BusinessSetup Setup(RunState run)
        {typeof(GameFlow).GetField("run",Hidden).SetValue(mapper,run);return mapper.BuildBusinessSetup();}

        // ---- 通用烹饪驱动：真实 BeginOrder/Action/Serve，帧级 Tick ----
        static void DriveLaneToPack(BusinessSession s,int lane)
        {
            for(int frame=0;frame<12000&&s.Lanes[lane].step!=KitchenStep.Pack;frame++)
            {
                var st=s.Lanes[lane];
                if(st.step==KitchenStep.Utensil)Need(s.Action(lane),"Utensil action refused");
                else if(st.step==KitchenStep.Season&&st.remaining<=0)Need(s.Action(lane),"Season action refused");
                s.Tick(1f/60f);
            }
            Need(s.Lanes[lane].step==KitchenStep.Pack,"lane never reached Pack, step="+s.Lanes[lane].step);
        }
        static StallOrder ArriveOneOrder(BusinessSession s)
        {
            for(int frame=0;frame<36000;frame++)
            {
                foreach(StallOrder o in s.Orders)if(!o.arrived&&!o.departed&&!o.served){Need(s.MarkCustomerArrived(o.id),"arrive refused");return o;}
                s.Tick(1f/60f);
            }
            throw new InvalidOperationException("no customer arrived");
        }
        static StallOrder WaitingOrder(BusinessSession s)
        {
            foreach(StallOrder o in s.Orders)if(o.arrived&&!o.departed&&!o.served&&o.lane<0&&o.reservedLane<0)return o;
            return null;
        }
        static void ServeOne(BusinessSession s,int lane,string package)
        {
            var order=WaitingOrder(s)??ArriveOneOrder(s);
            Need(order!=null,"no waiting arrived order to serve");
            Need(s.BeginOrder(order.id,lane),"BeginOrder refused: "+s.Message);
            if(s.Lanes[lane].step==KitchenStep.Pack){Need(s.Serve(lane,package),"Serve refused: "+s.Message);return;}
            DriveLaneToPack(s,lane);
            Need(s.Serve(lane,package),"Serve refused: "+s.Message);
        }

        // ---- A1 夹具：单锅(V09高汤桶)20份汤，客户耐心充足、来客快 ----
        static BusinessSession SoupFixture(int seasoning,int egg=30,int skewer=30,int packages=25,int gasPoints=45)
        {
            var setup=new BusinessSetup{stoveLoaded=true,cashCents=50000,seed=9021,dayDuration=420,gasPoints=gasPoints,customerIntervalSeconds=1.2f,
                patienceMinSeconds=35,patienceMaxSeconds=54,clearDownSeconds=35,lastCallNoticeSeconds=15,
                equipment=new HashSet<string>{"B04","E03","V09","U11","U12"},
                inventory=new Dictionary<string,int>{{"egg",egg},{"skewer",skewer},{"seasoning",seasoning},{"P07",packages}}};
            var s=new BusinessSession(setup);
            Need(s.SelectedRecipes.Count==1&&s.SelectedRecipes[0]==3,"fixture expected soup-only menu, got "+s.SelectedRecipes.Count);
            Need(s.Missing(3)=="","soup fixture should be hard-ready: "+s.Missing(3));
            return s;
        }

        static string SoftGates()
        {
            var s=SoupFixture(seasoning:0);
            string[][] gearPerRecipe={
                new[]{"B04","E03","V01","U01","P02"},          // fried_rice
                new[]{"B08","E05","RiceBucket","U14","P02"},    // prepared_rice (微波)
                new[]{"B04","E01","V05","U04","P04"},           // flatbread (煤炉)
                new[]{"B04","E03","V09","U11","P07"},           // soup
                new[]{"B04","E03","V13","U08","U10","P03"},     // fried_skewer (需U10)
                new[]{"V09","U12","P08"}};                      // plum_drink (冷饮无热源)
            var stock=new Dictionary<string,int>{{"rice",60},{"egg",60},{"dough",60},{"skewer",60},{"prepared",60},{"fruit",60},{"P02",60},{"P04",60},{"P07",60},{"P03",60},{"P08",60}};
            Need(SeasoningShortageRules.FlavorEffectPerShortPortion==-10&&SeasoningShortageRules.IntegrityDeltaPerShortPortion==-10,
                "production constants for 味效/诚信 no longer -10");
            for(int i=0;i<6;i++)
            {
                var probe=new BusinessSetup{stoveLoaded=true,cashCents=10000,seed=7,dayDuration=120,gasPoints=45,cartridgePoints=15,coalPoints=30,electricityPoints=45,
                    equipment=new HashSet<string>(gearPerRecipe[i]),inventory=new Dictionary<string,int>(stock)};
                var p=new BusinessSession(probe);
                Need(p.Missing(i)=="",BusinessSession.Recipes[i].id+" hard-blocked with only seasoning missing: "+p.Missing(i));
                Need(p.SoftMissing(i)!=null&&p.SoftMissing(i)!="",BusinessSession.Recipes[i].id+" soft warning absent");
                Need(!p.SeasoningAvailable,"SeasoningAvailable should be false at 0 stock");
            }
            Need(s.ToggleRecipe(3)&&!s.SelectedRecipes.Contains(3),"toggle soup off refused");
            Need(s.ToggleRecipe(3),"toggle soup on refused");Need(s.SelectedRecipes.Contains(3),"soup not selectable at seasoning 0");
            Need(s.Open(),"open refused at seasoning 0: "+s.Message);
            // 硬门槛逐项：必须拒绝并给出非空原因（不绑中文文案），恢复该件后可过
            void HardCase(int idx,string[] gear,Dictionary<string,int> inv,string removeStock)
            {
                inv.Remove(removeStock);gear=gear.Where(x=>x!=removeStock).ToArray();
                var h=new BusinessSetup{stoveLoaded=true,cashCents=10000,seed=7,dayDuration=120,gasPoints=45,cartridgePoints=15,coalPoints=30,electricityPoints=45,
                    equipment=new HashSet<string>(gear),inventory=new Dictionary<string,int>(inv)};
                var hp=new BusinessSession(h);
                Need(hp.Missing(idx)!="",BusinessSession.Recipes[idx].id+" not blocked without "+removeStock);
                Need(!hp.ToggleRecipe(idx)||!hp.SelectedRecipes.Contains(idx),"recipe selectable with hard miss "+removeStock);
                Need(hp.Message!="","no rejection reason recorded");
            }
            HardCase(0,new[]{"B04","E03","V01","U01"},new Dictionary<string,int>{{"rice",9},{"egg",9},{"P02",9}},"egg");
            HardCase(0,new[]{"B04","E03","V01","U01"},new Dictionary<string,int>{{"rice",9},{"egg",9},{"P02",9}},"P02");
            HardCase(2,new[]{"B04","E01","V05","U04"},new Dictionary<string,int>{{"dough",9},{"skewer",9},{"P04",9}},"dough");
            HardCase(3,new[]{"B04","E03","V09","U11"},new Dictionary<string,int>{{"egg",9},{"skewer",9},{"P07",9}},"V09");
            var noGas=new BusinessSession(new BusinessSetup{stoveLoaded=true,cashCents=10000,seed=7,dayDuration=120,gasPoints=0,
                equipment=new HashSet<string>{"B04","E03","V09","U11"},inventory=new Dictionary<string,int>{{"egg",9},{"skewer",9},{"P07",9}}});
            Need(noGas.Missing(3)!="","gas 0 not hard-blocked");
            var sk=new BusinessSession(new BusinessSetup{stoveLoaded=true,cashCents=10000,seed=7,dayDuration=120,gasPoints=45,
                equipment=new HashSet<string>{"B04","E03","V13","U08"},inventory=new Dictionary<string,int>{{"skewer",9},{"P03",9}}});
            Need(sk.Missing(4)!="","fried_skewer without U10 not blocked");
            return "6/6 recipes selectable with seasoning=0 (incl. prepared_rice & plum_drink); soft warning present; hard gates block 主料/包材/饼胚/锅具/煤气余量/食品夹 with reasons; open path works";
        }

        static string SoupTwentySeasoningZero()
        {
            var s=SoupFixture(seasoning:0);
            int cash0=s.CashCents;Need(s.Open(),"open refused");
            var first=ArriveOneOrder(s);Need(s.BeginOrder(first.id,0),"first BeginOrder refused: "+s.Message);
            Need(s.Lanes[0].batchRemaining==20,"soup batch expected 20, actual "+s.Lanes[0].batchRemaining);
            Need(s.Count("seasoning")==0&&s.Count("egg")==10,"BeginOrder must not debit seasoning; egg 30->10 actual "+s.Count("egg"));
            DriveLaneToPack(s,0);
            for(int portion=1;portion<=20;portion++)
            {
                Need(s.Serve(0,"P07"),"serve #"+portion+" refused: "+s.Message);
                Need(s.Count("seasoning")==0,"seasoning went negative at portion "+portion);
                Need(s.IntegrityChange==-10*portion,"IntegrityChange expected "+(-10*portion)+", actual "+s.IntegrityChange);
                Need(s.UnseasonedPortionsServed==portion,"Unseasoned expected "+portion+", actual "+s.UnseasonedPortionsServed);
                if(portion<20)
                {
                    var next=WaitingOrder(s)??ArriveOneOrder(s);
                    Need(s.BeginOrder(next.id,0),"next portion bind refused: "+s.Message);
                    DriveLaneToPack(s,0);
                }
            }
            Need(s.Served==20&&s.RevenueCents==20000,"served/revenue mismatch: "+s.Served+"/"+s.RevenueCents);
            Need(s.CashCents==cash0+20000,"base price changed on short seasoning: cash "+s.CashCents+" expected "+(cash0+20000));
            var audit=s.CaptureAudit();
            Need(audit.cashConserved&&audit.packagesConserved&&audit.nonNegativeEnergy&&audit.nonNegativeInventory,"audit conservation failed");
            s.Close();
            return "batch20 at seasoning0: 20 serves × base price 1000c, IntegrityChange=-200, Unseasoned=20, cash +20000, audit conserved";
        }

        static string SoupTwentySeasoningOne()
        {
            var s=SoupFixture(seasoning:1);
            Need(s.Open(),"open refused");
            ServeOne(s,0,"P07");
            Need(s.Count("seasoning")==0&&s.IntegrityChange==0&&s.UnseasonedPortionsServed==0,
                "portion1 with 1 stock must consume it without penalty; actual integrity="+s.IntegrityChange);
            for(int portion=2;portion<=20;portion++)
            {
                ServeOne(s,0,"P07");
                Need(s.IntegrityChange==-10*(portion-1),"per-portion independence broken at portion "+portion+": "+s.IntegrityChange);
            }
            Need(s.UnseasonedPortionsServed==19,"expected 19 short portions, actual "+s.UnseasonedPortionsServed);
            s.Inventory["seasoning"]=10;s.Inventory["egg"]=20;s.Inventory["skewer"]=20; // fixture restock between batches
            ServeOne(s,0,"P07");
            Need(s.UnseasonedPortionsServed==19&&s.IntegrityChange==-190,"short-mark leaked into restocked batch");
            Need(s.Count("seasoning")==9,"restocked seasoning not debited per serve");
            s.Close();
            return "seasoning1: portion1 clean, portions2-20 penalized (-190 total), restock clears marking, per-portion debit 10->9";
        }

        static string SaveBoundaries()
        {
            var s=SoupFixture(seasoning:0);Need(s.Open(),"open a");ArriveOneOrder(s);
            var pre=Disk(s.CaptureSave());
            var ra=BusinessSession.Restore(pre);
            Need(ra.Count("egg")==30&&ra.IntegrityChange==0&&ra.UnseasonedPortionsServed==0&&ra.RevenueCents==0,"pre-contact restore mutated state");
            var order=ra.Orders[0];Need(ra.BeginOrder(order.id,0),"restore begin refused");Need(ra.Lanes[0].batchRemaining==20,"batch lost on restore");
            var prep=Disk(ra.CaptureSave());var rb=BusinessSession.Restore(prep);
            Need(rb.Count("egg")==10&&rb.Count("seasoning")==0&&rb.Lanes[0].batchRemaining==20,"prep boundary mismatch");
            DriveLaneToPack(rb,0);var seasoned=Disk(rb.CaptureSave());var rc=BusinessSession.Restore(seasoned);
            Need(rc.Lanes[0].step==KitchenStep.Pack&&rc.Lanes[0].seasoned,"seasoned boundary mismatch");
            Need(rc.Serve(0,"P07"),"serve on restored refused");
            Need(rc.IntegrityChange==-10&&rc.UnseasonedPortionsServed==1,"restored session lost short facts");
            var mid=Disk(rc.CaptureSave());var rd=BusinessSession.Restore(mid);
            Need(rd.IntegrityChange==-10&&rd.UnseasonedPortionsServed==1&&rd.RevenueCents==1000&&rd.CashCents==51000,"mid-ledger restore mismatch cash="+rd.CashCents);
            for(int i=0;i<19;i++)ServeOne(rd,0,"P07");
            Need(rd.Served==20&&rd.RevenueCents==20000,"final serves mismatch");
            var done=Disk(rd.CaptureSave());var re=BusinessSession.Restore(done);
            Need(re.Served==20&&re.RevenueCents==20000&&re.IntegrityChange==-200&&re.UnseasonedPortionsServed==20,"restore double-applied seasoning ledger");
            Need(JsonUtility.ToJson(re.CaptureSave())==JsonUtility.ToJson(done),"round-trip changed ledger fields");
            return "boundaries a-e restore exactly (batch 20, egg 30->10, cash 50000->70000, IntegrityChange 0->-200 once); no re-application on restore";
        }

        static BusinessSaveData Disk(BusinessSaveData d)=>JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(d));

        // ---- 旧 v2 账义夹具：真实 CaptureSave 驱动到“锅内批次+已成交”态，再把
        // 库存改写为旧算法账义（旧 BeginOrder 已整批扣 20 油盐、旧 Serve 不扣），
        // 并移除新版才存在的全部字段，得到与 baseline/source-before.zip 旧写盘
        // 语义等价的 JSON（字段完全不存在，而非置0）。----
        static string OldV2Json(BusinessSaveData captured,int oldSeasoningStock)
        {
            foreach(var e in captured.inventory)if(e.id==SeasoningShortageRules.BaseSeasoningId)e.count=Mathf.Max(0,oldSeasoningStock);
            string json=JsonUtility.ToJson(captured);
            json=StripAll(json,"seasoningChargeVersion");
            json=StripAll(json,"prepaidSeasoning");
            json=StripAll(json,"unseasonedPortionsServed");
            json=StripAll(json,"integrityChange");
            return json;
        }
        static string StripAll(string json,string name)
        {
            // These four fields are scalar integers. Consume at most their own
            // separator, never the comma after a nested object's closing brace.
            string pattern="(?<before>[{,])\\s*\""+Regex.Escape(name)+"\"\\s*:\\s*-?\\d+\\s*(?<after>,?)";
            return Regex.Replace(json,pattern,m=>m.Groups["after"].Value==","?m.Groups["before"].Value:m.Groups["before"].Value=="{"?"{":"");
        }
        // 旧v2汤锅夹具：preSold 份已成交（旧义不扣油盐），锅内批份做熟到 Pack
        // （订单仍绑定，未离场——真实旧档可保存的形态）。返回旧义 JSON。
        static string OldSoupV2Json(int initialSeasoning,int preSold,out int batchRemaining,out int served,out int revenue,out int cash,out int oldStock)
        {
            var s=SoupFixture(seasoning:initialSeasoning);
            Need(s.Open(),"old fixture open refused");
            for(int i=0;i<preSold;i++)ServeOne(s,0,"P07");
            if(preSold>0)Need(s.Lanes[0].step==KitchenStep.ReadyStock,"pre-sold pan state mismatch");
            var order=WaitingOrder(s)??ArriveOneOrder(s);
            Need(s.BeginOrder(order.id,0),"old batch BeginOrder refused: "+s.Message);
            if(s.Lanes[0].step!=KitchenStep.Pack)DriveLaneToPack(s,0);
            Need(s.Lanes[0].step==KitchenStep.Pack,"old batch not cooked to Pack, step="+s.Lanes[0].step);
            batchRemaining=s.Lanes[0].batchRemaining;served=s.Served;revenue=s.RevenueCents;cash=s.CashCents;
            oldStock=Mathf.Max(0,initialSeasoning-20); // 旧 BeginOrder 整批扣20；旧 Serve 不扣
            return OldV2Json(s.CaptureSave(),oldStock);
        }
        static BusinessSession RestoreOld(string json)=>BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(json));

        static string MigrateFullPot()
        {
            string json=OldSoupV2Json(20,0,out int batch,out _,out _,out _,out int oldStock);
            Need(batch==20&&oldStock==0,"fixture algebra mismatch");
            var r=RestoreOld(json);
            var lane=r.Lanes[0];
            Need(lane.batchRemaining==20,"migrated batch mismatch");
            Need(lane.prepaidSeasoning==20,"acceptance requires full in-pan batch credit (old batch already debited); actual "+lane.prepaidSeasoning);
            int cash0=r.CashCents;
            Need(r.Serve(0,"P07"),"serve bound order refused: "+r.Message);
            for(int portion=2;portion<=20;portion++)ServeOne(r,0,"P07");
            Need(r.Count(SeasoningShortageRules.BaseSeasoningId)==0,"double debit of seasoning across migration");
            Need(r.IntegrityChange==0,"false penalty on prepaid portions: "+r.IntegrityChange);
            Need(r.UnseasonedPortionsServed==0,"false unseasoned mark on prepaid portions");
            Need(lane.prepaidSeasoning==0,"prepaid not exhausted to 0");
            Need(r.RevenueCents==20000&&r.CashCents==cash0+20000,"price/cash changed across migration");
            return "old v2 pot20 stock0: credit=20, 20 serves no double debit, no penalty, base price kept";
        }

        static string MigratePartialSold()
        {
            string json=OldSoupV2Json(25,5,out int batch,out int served,out int revenue,out int cash,out int oldStock);
            Need(batch==15&&served==5&&revenue==5000&&cash==55000&&oldStock==5,"fixture algebra mismatch b="+batch+" s="+served+" rev="+revenue+" stock="+oldStock);
            var r=RestoreOld(json);
            Need(r.Served==5&&r.RevenueCents==5000&&r.CashCents==55000,"read-only facts changed by migration");
            Need(r.Lanes[0].prepaidSeasoning==15,"partial in-pan credit expected 15, actual "+r.Lanes[0].prepaidSeasoning);
            Need(r.Serve(0,"P07"),"partial save's already-bound portion refused");
            for(int portion=1;portion<15;portion++)ServeOne(r,0,"P07");
            Need(r.RevenueCents==20000,"revenue mismatch after remaining batch");
            Need(r.Count(SeasoningShortageRules.BaseSeasoningId)==5,"seasoning stock moved: old debit must not be re-charged");
            Need(r.IntegrityChange==0&&r.UnseasonedPortionsServed==0,"penalty on historically-paid portions");
            return "old partial(5 sold, stock=25-20=5): facts kept, remaining 15 serves charge nothing, stock stays 5, integrity untouched";
        }

        static string MigrateResaveIdempotent()
        {
            string json=OldSoupV2Json(20,0,out _,out _,out _,out _,out _);
            var r=RestoreOld(json);
            Need(r.Serve(0,"P07"),"first serve refused");
            string resaved=JsonUtility.ToJson(r.CaptureSave());
            Need(resaved.Contains("seasoningChargeVersion"),"post-migration save not marked v3");
            var r2=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(resaved));
            Need(r2.Lanes[0].prepaidSeasoning==r.Lanes[0].prepaidSeasoning,"prepaid changed across resave/restore");
            ServeOne(r2,0,"P07");
            string again=JsonUtility.ToJson(r2.CaptureSave());
            var r3=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(again));
            for(int i=0;i<18;i++)ServeOne(r3,0,"P07");
            Need(r3.RevenueCents==20000&&r3.IntegrityChange==0,"repeated save/restore double-charged");
            Need(r3.Lanes[0].prepaidSeasoning==0,"prepaid not exhausted after final serves");
            return "migrate->save(v3)->restore x2->finish: single charge overall, prepaid monotone decreasing to 0";
        }

        static string MigratePreparedColdNoCredit()
        {
            // 预制饭：旧配方无 seasoning，库存0 — 不得虚构 credit；新成交按新规则处罚
            var prep=new BusinessSession(new BusinessSetup{stoveLoaded=true,cashCents=20000,seed=31,dayDuration=200,electricityPoints=45,customerIntervalSeconds=1.2f,
                patienceMinSeconds=35,patienceMaxSeconds=54,equipment=new HashSet<string>{"B08","E05","RiceBucket","U14"},
                inventory=new Dictionary<string,int>{{"rice",10},{"prepared",10},{"P02",10},{"seasoning",0}}});
            Need(prep.SelectedRecipes.Count==1&&prep.SelectedRecipes[0]==1,"prepared fixture menu mismatch");
            Need(prep.Open(),"prepared open refused");
            var o1=WaitingOrder(prep)??ArriveOneOrder(prep);
            Need(prep.BeginOrder(o1.id,0),"prepared begin refused: "+prep.Message);
            DriveLaneToPack(prep,0);
            string oldJson=OldV2Json(prep.CaptureSave(),0);
            var rp=RestoreOld(oldJson);
            Need(rp.Lanes[0].prepaidSeasoning==0,"fabricated credit for old no-seasoning recipe");
            Need(rp.Serve(0,"P02"),"prepared serve refused: "+rp.Message);
            Need(rp.IntegrityChange==-10&&rp.UnseasonedPortionsServed==1,"new-rule serve at stock0 should record short fact");
            // 冷饮同样不虚构 credit；库存有时正常每份扣1
            var cold=new BusinessSession(new BusinessSetup{stoveLoaded=true,cashCents=20000,seed=37,dayDuration=200,customerIntervalSeconds=1.2f,
                patienceMinSeconds=35,patienceMaxSeconds=54,equipment=new HashSet<string>{"V09","U12"},
                inventory=new Dictionary<string,int>{{"fruit",30},{"P08",30},{"seasoning",3}}});
            Need(cold.SelectedRecipes.Count==1&&cold.SelectedRecipes[0]==5,"cold fixture menu mismatch");
            Need(cold.Open(),"cold open refused");
            var o5=WaitingOrder(cold)??ArriveOneOrder(cold);
            Need(cold.BeginOrder(o5.id,0),"cold begin refused: "+cold.Message);
            DriveLaneToPack(cold,0);
            string coldJson=OldV2Json(cold.CaptureSave(),3);
            var rc=RestoreOld(coldJson);
            Need(rc.Lanes[0].prepaidSeasoning==0,"fabricated credit for cold drink");
            Need(rc.Serve(0,"P08"),"cold serve refused: "+rc.Message);
            Need(rc.Count("seasoning")==2&&rc.IntegrityChange==0,"cold serve with stock must debit 1, not credit");
            return "prepared/cold old saves: no fabricated credit; stock0 serve penalized -10, stock3 serve debits to 2";
        }

        static string MigrateIdleDirtyAndInvalidCredit()
        {
            // Idle（未投料）旧档：不建 credit，可正常Restore
            var s=SoupFixture(seasoning:0);Need(s.Open(),"idle open");
            string idleJson=OldV2Json(s.CaptureSave(),0);
            var ri=RestoreOld(idleJson);
            Need(ri.Lanes.Length==0||ri.Lanes[0].prepaidSeasoning==0,"credit on idle lane");
            // Dirty（旧报废台账守恒）旧档：不建 credit
            var d=SoupFixture(seasoning:20);Need(d.Open(),"dirty open");
            var od=WaitingOrder(d)??ArriveOneOrder(d);
            Need(d.BeginOrder(od.id,0),"dirty batch begin refused");
            for(int frame=0;frame<3600&&d.Lanes[0].step!=KitchenStep.Dirty;frame++)d.Tick(1f/60f);
            Need(d.Lanes[0].step==KitchenStep.Dirty&&d.WastedPortions==20,"actual burn failed to reach dirty conserved batch");
            var dirty=d.CaptureSave();
            string dirtyJson=OldV2Json(dirty,0);
            var rd=RestoreOld(dirtyJson);
            Need(rd.Lanes[0].prepaidSeasoning==0,"credit on dirty lane");
            // 新档非法 credit 拒绝（强类型构造，非反射）
            var fresh=SoupFixture(seasoning:5);Need(fresh.Open(),"invalid open");
            var live=ArriveOneOrder(fresh);Need(fresh.BeginOrder(live.id,0),"invalid-credit baseline begin");DriveLaneToPack(fresh,0);
            var valid=Disk(fresh.CaptureSave());BusinessSession.Restore(valid);
            var bad=Disk(fresh.CaptureSave());
            bad.lanes[0].prepaidSeasoning=bad.lanes[0].batchRemaining+1;
            NeedReject(()=>BusinessSession.Restore(bad),"prepaid>batchRemaining accepted");
            var neg=Disk(fresh.CaptureSave());
            neg.lanes[0].prepaidSeasoning=-1;
            NeedReject(()=>BusinessSession.Restore(neg),"negative prepaid accepted");
            return "idle/dirty old saves carry no credit (dirty waste ledger conserved); prepaid>batchRemaining and prepaid<0 rejected";
        }

        static string NewBatchNoInherit()
        {
            string json=OldSoupV2Json(20,0,out _,out _,out _,out _,out _);
            var r=RestoreOld(json);
            Need(r.Serve(0,"P07"),"first serve refused");
            for(int i=1;i<20;i++)ServeOne(r,0,"P07");
            Need(r.Lanes[0].batchRemaining==0&&r.Lanes[0].prepaidSeasoning==0,"migrated batch not settled");
            r.Inventory["seasoning"]=10;r.Inventory["egg"]=20;r.Inventory["skewer"]=20; // fixture restock
            ServeOne(r,0,"P07"); // 新锅（Idle->BeginOrder->Cook->Serve 1份）
            Need(r.Lanes[0].prepaidSeasoning==0,"new batch inherited prepaid credit");
            Need(r.Count("seasoning")==9,"new batch not charged per new rule (10->9)");
            Need(r.IntegrityChange==0,"penalty in stocked new batch");
            return "post-migration new batch: prepaid=0, per-serve stock debit (10->9), no penalty";
        }

        // ---- A2：采购与推荐（真实 RunState 交易路径）----
        static RunState MarketRun(RunProfile profile)
        {
            var run=new RunState(4,profile);run.Paused=false;
            Need(run.Act("HomeExit",true),"cannot enter market: "+run.Message);
            return run;
        }
        static void LoadHome(RunState run)
        {
            run.Paused=false;
            for(int guard=0;run.HasUnloadedEquipment&&guard<20;guard++)
            {Need(run.Act("StovePickup",true),run.Message);Need(run.Act("HomeCart",true),"Home load: "+run.Message);}
            Need(!run.HasUnloadedEquipment,"Home equipment did not finish loading");
        }
        static void Pay(RunState run)
        {
            int cash=run.CashCents,spent=run.SpentCents,total=run.CartTotalCents;
            Need(run.Act("Cashier",true),"checkout refused: "+run.Message);
            run.Tick(run.CheckoutDuration,true);
            Need(run.Paid&&run.CashCents==cash-total&&run.SpentCents==spent+total,"checkout debit mismatch: cash "+run.CashCents+" expected "+(cash-total));
        }
        // 货架原料（GameCatalog）真实抱箱入篮路径
        static void ShelfBasket(RunState run,string id)
        {
            Need(run.Act(GameCatalog.Find(id).anchor,true,false),"pickup "+id+": "+run.Message);
            Need(run.HeldGrocery==id,"held mismatch");
            Need(run.Act("ShoppingCart",true),"stow "+id+": "+run.Message);
        }

        static string RecommendCompatible()
        {
            var profile=Profile("career4");
            Need(profile.Gear.Contains("B03")&&profile.Gear.Contains("V02"),"career4 gear assumption changed");
            var run=MarketRun(profile);
            Need(run.HomeEquipment.Contains("B03")&&!run.LoadedEquipment.Contains("B03"),"fixture expects B03 home");
            Need(run.LoadedEquipment.Contains("V02"),"fixture expects V02 aboard");
            var before=string.Join(",",run.LoadedEquipment);
            var plan=OpeningProcurementPlan.For(run);
            var owned=new HashSet<string>(run.LoadedEquipment);owned.UnionWith(run.Cargo);owned.UnionWith(run.Cart);
            foreach(var stock in run.Supplies)if(stock.Value>0)owned.Add(stock.Key);
            int cash0=run.CashCents;
            foreach(string id in plan.supplies)if(!owned.Contains(id))Need(run.AddSupply(id),"plan supply "+id+" refused: "+run.Message);
            foreach(string id in plan.shelves)if(!owned.Contains(id))ShelfBasket(run,id);
            Pay(run);
            Need(run.Act("MarketCart",true),"recommended paid goods did not load: "+run.Message);
            var setup=Setup(run);
            int idx=MenuIndex(plan.menu);
            string missing=new BusinessSession(setup).Missing(idx);
            Need(missing=="","plan produces illegal/incomplete setup for "+plan.menu+": "+missing);
            Need(string.Join(",",run.LoadedEquipment)==before,"recommendation moved home equipment for free");
            Need(run.HomeEquipment.Contains("B03"),"recommendation consumed home B03");
            return "plan '"+plan.menu+"' feasible via real purchase (cash "+cash0+"->"+run.CashCents+"); B03 stays home; no free gear move";
        }
        static string RecommendGaps()
        {
            // Isolated ownership fixture: microwave, battery and the durable
            // rice bucket aboard; the recipe's required scissors (U14) are
            // initially absent. RiceBucket is declared owned career-grade
            // gear — not purchased, no money/stock injected. Money remains
            // career cash. Without it the newly restored retail goods
            // (U01 ¥5 etc.) legitimately price a cheaper fried_rice counter
            // plan, which is verified as a separate purchasable scenario
            // below; with it the microwave plan is the cheapest complete one
            // on price alone, no career binding involved.
            var initial=new RunState(4,Profile("career3")).CaptureSave();
            initial.loadedEquipment=new[]{"B08","E05","RiceBucket"};initial.homeEquipment=Array.Empty<string>();
            var run=RunState.Restore(initial);run.Paused=false;Need(run.Act("HomeExit",true),"gap fixture market entry");
            var plan=OpeningProcurementPlan.For(run);
            Need(plan.shelves.Contains("prepared"),"gap fixture must include real shelf goods");
            int quoted=plan.supplies.Concat(plan.shelves).Distinct().Sum(id=>run.PriceCents(id));
            Need(plan.totalCents==quoted,"plan omits shelf cost: "+plan.totalCents+" vs "+quoted);
            Need(plan.Affordable(run)==(quoted<=run.CashCents),"affordability differs from complete quotation");
            Need(plan.recipeIndex==1&&plan.supplies.Contains("U14"),"advisor did not fill missing microwave scissors");
            Need(plan.totalCents==10600,"microwave plan price drifted: U14 1500 + rice_small 500 + seasoning_small 500 + prepared 8100, actual "+plan.totalCents);
            foreach(string id in plan.supplies)Need(run.AddSupply(id),"gap buy "+id+": "+run.Message);
            foreach(string id in plan.shelves)ShelfBasket(run,id);
            Pay(run);Need(run.Act("MarketCart",true),"gap paid goods failed to load");
            string actual=new BusinessSession(Setup(run)).Missing(1);
            Need(actual==""&&plan.gaps=="","recommended loaded microwave setup still blocked: "+actual);
            // A real unpaid basket filled with nine 1-cell tools/vessels has no
            // food or packaging. Every complete food plan needs more space.
            var full=MarketRun(Profile("career1"));
            foreach(string id in new[]{"U04","U06","U10","U08","U11","U12","V01","V05","V09"})Need(full.AddSupply(id),"full-gap basket "+id);
            Need(full.CartCellsUsed==full.CartCapacity,"gap fixture not actually full");
            var blocked=OpeningProcurementPlan.For(full);
            Need(!string.IsNullOrEmpty(blocked.gaps),"full basket plan did not explain capacity gap");
            int cash=full.CashCents,count=full.Cart.Count;
            Need(blocked.supplies.Length>0&&!full.AddSupply(blocked.supplies[0]),"reported gap did not prevent actual additional purchase");
            Need(full.CashCents==cash&&full.Cart.Count==count,"gap rejection changed cash or basket");
            // The pre-restoration B08/E05-only ownership (no RiceBucket) now
            // legitimately prices a cheaper fried_rice plan (10600 < 14600)
            // thanks to the restored retail U01 ¥5. Prove that cheaper
            // recommendation is a real purchasable, openable setup — the
            // production rule stays "cheapest complete plan", not a career
            // hard-bind to prepared_rice.
            var leanInit=new RunState(4,Profile("career3")).CaptureSave();
            leanInit.loadedEquipment=new[]{"B08","E05"};leanInit.homeEquipment=Array.Empty<string>();
            var leanRun=RunState.Restore(leanInit);
            var lean=OpeningProcurementPlan.For(leanRun);
            Need(lean.recipeIndex==0&&lean.gaps==""&&lean.Affordable(leanRun),
                "B08/E05-only ownership should price a feasible cheaper fried_rice plan, got recipe "+lean.recipeIndex+" gaps '"+lean.gaps+"'");
            Need(lean.totalCents==11100,"cheaper counter plan price drifted: U01 500 + rice_small 500 + egg_small 600 + V01 3500 + B01 3500 + E01 2000 + seasoning_small 500, actual "+lean.totalCents);
            var leanCheck=ProcurementPlanCheck.Project(leanRun,lean.supplies,lean.shelves);
            Need(leanCheck.Missing(leanRun,lean.recipeIndex)=="","cheaper plan does not form a valid setup: "+leanCheck.Missing(leanRun,lean.recipeIndex));
            return "microwave scissors filled, supplies+shelf cost="+quoted+" cents; real checkout/load opens recipe; full-basket gap='"+blocked.gaps+"' matches actual rejected purchase; B08/E05-only lean plan (recipe "+lean.recipeIndex+", "+lean.totalCents+"c) also valid without career binding";
        }
        static int MenuIndex(string menu)
        {
            for(int i=0;i<BusinessSession.Recipes.Length;i++)if(BusinessSession.Recipes[i].name.Contains(menu))return i;
            throw new InvalidOperationException("unknown plan menu "+menu);
        }

        static string BasketNoPay()
        {
            var run=MarketRun(Profile("career1"));
            int cash=run.CashCents;var suppliesBefore=string.Join(";",run.Supplies.OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value));
            Need(run.AddSupply("rice_small"),"add rice_small refused");
            Need(run.AddSupply("P02"),"add P02 refused");
            Need(run.CashCents==cash,"basket entry changed cash");
            Need(string.Join(";",run.Supplies.OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value))==suppliesBefore,"basket entry changed stock");
            Need(run.Cart.Count==2,"basket count mismatch");
            Need(run.AddSupply("rice_small"),"repeat consumable refused");
            Need(run.Cart.Count(x=>x=="rice_small")==2,"repeat cartons not preserved");
            Need(run.AddSupply("U01")&&!run.AddSupply("U01"),"durable equipment duplicate rule failed");
            Need(run.CashCents==cash,"duplicate changed cash");
            foreach(var item in SupplyCatalog.Items)
            {
                if(run.Cart.Contains(item.id)||run.HomeEquipment.Contains(item.id)||run.LoadedEquipment.Contains(item.id))continue;
                if(!run.CanFitCart(item.id))continue;
                Need(run.AddSupply(item.id),"fill add "+item.id+" refused");
            }
            int cells=run.CartCellsUsed;
            Need(cells>=run.CartCapacity,"cart never filled: "+cells+"/"+run.CartCapacity);
            Need(!run.AddSupply("V05"),"overfull cart accepted");
            Need(run.CashCents==cash,"full-basket rejection changed cash");
            Need(!run.Paid,"basket-only fixture must stay unpaid");
            return "entry only in basket (cash "+cash+" unchanged, stock unchanged); consumables repeat / durable duplicate refused; full cart ("+cells+"/"+run.CartCapacity+") rejects without side effects";
        }

        static string EnergyRefill()
        {
            var profile=Profile("career5"); // 无炉无锅职业，避免自带热源干扰
            var baseRun=new RunState(4,profile);var data=baseRun.CaptureSave();
            data.loadedEquipment=new[]{"B01","V01","U01","E01"};data.coal=0;data.gas=0;data.cartridge=0;data.supplies=new[]{new RunStockSave{id="rice",quantity=10},new RunStockSave{id="egg",quantity=10},new RunStockSave{id="P02",quantity=20}};
            var run=RunState.Restore(data);run.Paused=false;Need(run.Act("HomeExit",true),"market entry refused");
            Need(run.CoalPoints==0,"coal fixture mismatch");
            var plan=OpeningProcurementPlan.For(run);
            Need(plan.supplies.Contains("E01"),"coal0 with E01 owned: plan must recommend coal refill, got ["+string.Join(",",plan.supplies)+"]");
            Need(run.AddSupply("E01"),"coal0 refill AddSupply refused: "+run.Message);
            var data2=data;data2.coal=12;var run3=RunState.Restore(data2);run3.Paused=false;Need(run3.Act("HomeExit",true),"market3 refused");
            Need(!run3.AddSupply("E01"),"coal>0 duplicate E01 accepted");
            Need(new BusinessSession(Setup(run3)).Missing(0)=="","coal12 fried_rice wrongly blocked");
            data2.coal=0;var run4=RunState.Restore(data2);run4.Paused=false;
            Need(new BusinessSession(Setup(run4)).Missing(0)!="", "coal0 not hard-blocked");
            var base3=new RunState(4,profile);var data3=base3.CaptureSave();
            data3.loadedEquipment=new[]{"B02","V01","U01","E03"};data3.gas=20;data3.cartridge=0;data3.supplies=new[]{new RunStockSave{id="rice",quantity=10},new RunStockSave{id="egg",quantity=10},new RunStockSave{id="P02",quantity=20}};
            var run5=RunState.Restore(data3);run5.Paused=false;
            Need(new BusinessSession(Setup(run5)).Missing(0)!="", "E03 faked cartridge supply");
            return "E01+coal0 -> plan recommends E01 & refill purchasable; coal12 -> no dup buy, session cookable; coal0 -> hard block; B02 cartridge0 with E03 aboard stays blocked";
        }

        // ---- A7/A3：日账守恒（真实付款采购 + 生产 CommitDay）----
        static string ZeroDayLedger()
        {
            var profile=Profile("career2");var run=new RunState(4,profile);run.Paused=false;
            var campaign=CampaignState.New(profile,run);
            LoadHome(run);
            Need(run.GasPoints==45,"career2 E03 gas assumption changed: "+run.GasPoints);
            Need(run.Act("HomeExit",true),"zero market entry refused: "+run.Message);
            foreach(string id in new[]{"V09","U11","P07","egg_small","seasoning_small"})Need(run.AddSupply(id),"zero-day basket "+id+" refused");
            ShelfBasket(run,"skewer");
            Pay(run);
            int opening=run.CashCents,spent=run.SpentCents;
            Need(run.Act("MarketCart",true),"load refused: "+run.Message);
            Need(run.Act("MarketExit",true),"exit refused: "+run.Message);
            var service=new BusinessSession(Setup(run));
            Need(service.SelectedRecipes.Contains(3),"zero-day soup not selectable");
            service.Close();Need(service.Finished&&!service.Started,"zero-day close should not start service");
            Need(service.RevenueCents==0&&service.Served==0,"zero-day phantom revenue");
            Need(campaign.CommitDay(run,service,service.SessionId),"zero day commit refused");
            var ledger=campaign.lastDay;
            Need(ledger.revenueCents==0&&ledger.sales.Count==0,"zero-day ledger has revenue");
            Need(ledger.purchasesCents==spent,"purchases mismatch: "+ledger.purchasesCents+" vs "+spent);
            Need(ledger.openingCashCents-spent==opening,"day opening was not captured before procurement");
            Need(campaign.cashCents==opening,"closing cash mismatch: "+campaign.cashCents+" expected "+opening);
            Need(ledger.closingCashCents==campaign.cashCents,"ledger closing != campaign cash");
            Need(ledger.nightFuelCents==0,"unexpected night fuel charge on zero day (tank full 45)");
            string frozen=JsonUtility.ToJson(campaign);
            Need(!campaign.CommitDay(run,service,service.SessionId)&&JsonUtility.ToJson(campaign)==frozen,"duplicate zero-day commit mutated campaign");
            var next=campaign.CreateNextDay(4);
            Need(next.CashCents==opening&&next.SpentCents==0,"next day cash mismatch: "+next.CashCents);
            Need(next.Supplies.TryGetValue("seasoning",out int s)&&s==10,"restocked seasoning lost overnight");
            Need(next.Supplies.TryGetValue("skewer",out int k)&&k==15,"shelf skewer lost overnight");
            return "zero day: opening="+ledger.openingCashCents+", after-pay="+opening+", spent="+spent+", revenue0, nightFuel0, commit idempotent, next-day cash/seasoning/skewer exact";
        }

        static string SalesDayLedger()
        {
            var profile=Profile("career2");var run=new RunState(4,profile);run.Paused=false;
            var campaign=CampaignState.New(profile,run);
            LoadHome(run);Need(run.GasPoints==45,"career2 E03 gas assumption changed");
            Need(run.Act("HomeExit",true),"sales market entry refused: "+run.Message);
            foreach(string id in new[]{"V09","U11","P07","egg_small"})Need(run.AddSupply(id),"sales basket "+id+" refused: "+run.Message);
            ShelfBasket(run,"skewer");
            Pay(run);
            int cashAfterPay=run.CashCents,spent=run.SpentCents;
            Need(run.Act("MarketCart",true),"sales load refused: "+run.Message);
            Need(run.Act("MarketExit",true),"sales exit refused");
            var setup=Setup(run);
            var service=new BusinessSession(setup);
            Need(service.SelectedRecipes.Contains(3),"soup not auto-selected: "+string.Join(",",service.SelectedRecipes));
            Need(service.Missing(3)=="","soup hard-miss after real purchase: "+service.Missing(3));
            Need(service.Count("seasoning")==0,"fixture expects no seasoning so serves record short facts");
            Need(service.Open(),"sales open refused");
            for(int i=0;i<3;i++)ServeOne(service,0,"P07");
            Need(service.IntegrityChange==-30&&service.UnseasonedPortionsServed==3,"short-serve facts mismatch");
            service.Close();
            Need(campaign.CommitDay(run,service,service.SessionId),"sales commit refused");
            var ledger=campaign.lastDay;
            int salesSum=ledger.sales.Sum(x=>x.totalCents);
            Need(ledger.revenueCents==3000&&salesSum==3000,"revenue/sales mismatch: "+ledger.revenueCents+"/"+salesSum);
            Need(ledger.nightFuelCents==2000,"used gas tank should refill for exactly 2000 cents");
            Need(campaign.cashCents==cashAfterPay+3000-ledger.nightFuelCents,"campaign cash != paid cash + revenue - nightly fuel");
            Need(ledger.closingCashCents==ledger.openingCashCents-ledger.purchasesCents+ledger.revenueCents-ledger.nightFuelCents,"full sales day cash conservation");
            Need(ledger.closingCashCents==campaign.cashCents,"ledger closing mismatch");
            foreach(var row in ledger.sales)Need(row.unitPriceCents==1000,"unit price changed: "+row.unitPriceCents);
            string frozen=JsonUtility.ToJson(campaign);
            Need(!campaign.CommitDay(run,service,service.SessionId)&&JsonUtility.ToJson(campaign)==frozen,"duplicate sales commit mutated campaign");
            return "sales day: 3 short soup portions, revenue=3000=salesSum, spent="+spent+", base price 1000c kept, commit idempotent";
        }
    }
}
