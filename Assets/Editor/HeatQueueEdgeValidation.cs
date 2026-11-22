using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Independent PlayMode acceptance for heat-queue edge cases against real
    // BusinessController / BusinessSession / StreetStallStage / VendorWorkActor:
    // 1) defer-then-cancel: MidgamePractice lane 0 real egg pickup -> stir_fry ->
    //    active uncommitted start-cook -> legal rice addition defers start-cook ->
    //    real CancelPendingWork cancels deferred start-cook while in-flight rice
    //    still contacts and debits exactly 1, gas stays unchanged, no silent restart.
    // 2) queue-capacity-guard: MidgamePractice lane 0 real egg pickup -> stir_fry ->
    //    active uncommitted start-cook + 7 reflection-injected SYNTHETIC placeholder
    //    intents in pendingWork (explicitly synthetic capacity fixture, NOT player
    //    operations) -> real UseIngredientAtLane(0, "rice") immediately rejected at
    //    capacity 8 with lane 1 available, active start-cook preserved, no premature
    //    stock/gas debit -> real CancelPendingWork clears placeholders -> frame-by-frame
    //    ticks complete original start-cook with exactly 1 gas debit and 0 rice debit.
    // Explicitly scripted Controller/Stage calls; NOT human mouse input, NOT OS hardware
    // input, and NOT visual/screen verification. Does not create or overwrite user saves.
    [InitializeOnLoad]
    public static class HeatQueueEdgeValidation
    {
        [Serializable]
        public sealed class CaseResult
        {
            public string name = "";
            public bool passed;
            public string detail = "";
            public string failure = "";
            public bool syntheticFixtureUsed;
            public string syntheticFixtureNote = "";
            public float elapsed;
            public BusinessAuditState final;
            public BusinessWorkAudit[] work;
        }

        [Serializable]
        public sealed class Report
        {
            public string schema = "guochao.heat_queue_edge.v1";
            public string scope = "Real Editor PlayMode Stage/actor acceptance with scripted Controller/Stage commands on BusinessSetup.MidgamePractice() in an isolated empty PlayMode scene (no user save creation or overwrite). Case 1 (defer-then-cancel) verifies real active uncommitted stir_fry start-cook deferred by legal rice addition and then cancelled via real CancelPendingWork while in-flight rice still contacts and debits exactly 1, gas debits 0, and start-cook does not silently restart. Case 2 (queue-capacity-guard) uses a reflection-injected SYNTHETIC capacity-boundary fixture (7 inert placeholder intents injected into BusinessController.pendingWork; explicitly synthetic and NOT player operations) to verify real UseIngredientAtLane(0, rice) is immediately rejected at capacity 8 with lane 1 available, original active start-cook is preserved without premature stock/gas debit, and after real CancelPendingWork clears placeholders, real frame-by-frame ticks complete original start-cook with exactly 1 gas debit and 0 rice debit. Explicitly NOT human mouse input, NOT OS hardware input, and NOT visual/screen verification.";
            public bool success;
            public int passed;
            public int failed;
            public string failure = "";
            public bool hardwareInput = false;
            public bool collisionDisabled = false;
            public bool infinitePatience = false;
            public List<CaseResult> cases = new List<CaseResult>();
            public string[] engineErrors = Array.Empty<string>();
        }

        sealed class Fixture
        {
            public GameObject root;
            public BusinessController controller;
            public StreetStallStage stage;
            public BusinessSession Session => controller.Session;
            public readonly List<string> failures = new List<string>();
        }

        const float Step = 1f / 60f;
        const int ExpectedCaseCount = 2;
        const string PendingKey = "Guochao.HeatQueueEdge.Pending";
        const string OutputKey = "Guochao.HeatQueueEdge.Output";

        static readonly List<string> errors = new List<string>();
        static Feedback3CoroutineHost frameHost;
        static Fixture live;
        static Report report;
        static string outputPath;
        static double beganAt;

        static HeatQueueEdgeValidation()
        {
            EditorApplication.update += Advance;
        }

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--heat-queue-edge-report");
            if (at < 0 || at + 1 >= args.Length || string.IsNullOrWhiteSpace(args[at + 1]))
                throw new ArgumentException("Specify --heat-queue-edge-report <path>");
            string path = Path.GetFullPath(args[at + 1]);
            SessionState.SetString(OutputKey, path);
            SessionState.SetBool(PendingKey, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void Advance()
        {
            if (EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
            if (SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                outputPath = SessionState.GetString(OutputKey, "");
                report = new Report();
                errors.Clear();
                beganAt = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += Log;
                frameHost = new GameObject("HeatQueueEdge_PlayerFrameHost").AddComponent<Feedback3CoroutineHost>();
                frameHost.Launch(RunAllCases(), () => Finish(""), ex => Finish(ex.ToString()));
            }
            if (frameHost != null && frameHost.IsRunning && EditorApplication.timeSinceStartup - beganAt > 240)
            {
                frameHost.Abort();
                Finish("HeatQueueEdgeValidation exceeded 240 seconds");
            }
        }

        static IEnumerator RunAllCases()
        {
            Need(GameFlow.Instance == null, "Isolated empty PlayMode scene required (GameFlow.Instance must be null)");
            string[] names = { "defer-then-cancel", "queue-capacity-guard" };
            Func<CaseResult, IEnumerator>[] tests = { DeferThenCancel, QueueCapacityGuard };
            for (int i = 0; i < tests.Length; i++)
            {
                var item = new CaseResult { name = names[i], passed = false };
                report.cases.Add(item);
                IEnumerator test = tests[i](item);
                bool done = false;
                while (!done)
                {
                    object yielded = null;
                    try
                    {
                        if (test.MoveNext()) yielded = test.Current;
                        else { item.passed = true; done = true; }
                    }
                    catch (Exception ex)
                    {
                        item.passed = false;
                        item.failure = ex.ToString();
                        item.detail = string.IsNullOrEmpty(item.detail)
                            ? "EXCEPTION: " + ex.Message
                            : item.detail + " | EXCEPTION: " + ex.Message;
                        done = true;
                    }
                    if (!done) yield return yielded;
                }
                if (live != null)
                {
                    try
                    {
                        item.elapsed = live.Session.Elapsed;
                        item.final = live.Session.CaptureAudit();
                        item.work = live.controller.CaptureWorkHistory();
                    }
                    catch (Exception captureEx)
                    {
                        item.passed = false;
                        item.failure = string.IsNullOrEmpty(item.failure)
                            ? captureEx.ToString()
                            : item.failure + "\n--- CaptureAudit ---\n" + captureEx;
                    }
                }
                (test as IDisposable)?.Dispose();
                DisposeFixture();
                yield return null;
            }
        }

        static Fixture Create(BusinessSetup setup)
        {
            Need(live == null, "Fixtures must not overlap");
            var f = new Fixture { root = new GameObject("HeatQueueEdge_RealStage") };
            live = f;
            var session = new BusinessSession(setup);
            session.SelectedRecipes.Clear();
            Need(session.Open(), "MidgamePractice free-batch session must open: " + session.Message);
            f.controller = f.root.AddComponent<BusinessController>();
            f.controller.enabled = false;
            f.stage = f.root.AddComponent<StreetStallStage>();
            f.stage.Initialize(session.Setup, null);
            f.stage.Configure(session);
            typeof(BusinessController).GetProperty("Session").SetValue(f.controller, session);
            typeof(BusinessController).GetProperty("Stage").SetValue(f.controller, f.stage);
            var input = f.root.AddComponent<StallInteractionInput>();
            input.Initialize(f.controller);
            input.enabled = false;
            typeof(BusinessController).GetProperty("InputHandler").SetValue(f.controller, input);
            f.stage.CustomerArrived += id => session.MarkCustomerArrived(id);
            f.stage.WorkFailed += message => f.failures.Add(message);
            VendorWorkActor actor = f.root.GetComponent<VendorWorkActor>();
            Need(actor != null && actor.IsBound && !actor.automaticTick, "Real manually-ticked VendorWorkActor required");
            int solids = 0;
            foreach (Collider collider in f.root.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger && collider.gameObject.layer == StreetStallStage.SolidLayer) solids++;
            Need(solids > 0, "Real SolidLayer collision must remain enabled");
            return f;
        }

        static void Tick(Fixture f, float dt = Step)
        {
            Physics.SyncTransforms();
            f.controller.AdvanceSimulation(dt);
            Need(f.failures.Count == 0, "Actor failure: " + string.Join(";", f.failures));
            Need(f.stage.WorkAudit != null && string.IsNullOrEmpty(f.stage.WorkAudit.lastFailure),
                "Actor rejected action: " + (f.stage.WorkAudit != null ? f.stage.WorkAudit.lastFailure : "null WorkAudit"));
            CheckInvariants(f.Session);
            Need(f.stage.WorkAudit.maxContactError <= 0.0251f,
                "Real contact missed tolerance: " + f.stage.WorkAudit.maxContactError.ToString("F4"));
        }

        static void CheckInvariants(BusinessSession session)
        {
            BusinessAuditState audit = session.CaptureAudit();
            Need(audit.cashConserved && audit.packagesConserved && audit.nonNegativeInventory && audit.nonNegativeEnergy,
                "Ledger invariant violated");
        }

        static int UnitsOf(KitchenLane lane, string itemId)
        {
            if (lane == null || lane.freeBatch == null || lane.freeBatch.inputs == null) return 0;
            foreach (var input in lane.freeBatch.inputs)
                if (input.itemId == itemId) return input.units;
            return 0;
        }

        static string DescribeLaneState(BusinessSession s, int laneIndex)
        {
            KitchenLane l = s.Lanes[laneIndex];
            string method = l.freeBatch != null ? (l.freeBatch.method ?? "") : "";
            string contents = s.LaneContents(laneIndex);
            return "lane" + laneIndex + "(step=" + l.step + ",method='" + method + "',contents='" + contents
                + "',egg=" + UnitsOf(l, "egg") + ",rice=" + UnitsOf(l, "rice") + ",batchRem=" + l.batchRemaining
                + ",energy='" + (l.energy ?? "") + "')";
        }

        static string FormatWorkSlice(BusinessWorkAudit[] history, int startIndex)
        {
            var parts = new List<string>();
            for (int i = startIndex; i < history.Length; i++)
            {
                var w = history[i];
                parts.Add("#" + w.serial + "(lane=" + w.lane + ",act=" + w.action + ",st=" + w.status
                    + ",q=" + w.queuedAt.ToString("F3") + ",s=" + w.startedAt.ToString("F3")
                    + ",c=" + w.contactAt.ToString("F3") + ",f=" + w.finishedAt.ToString("F3") + ")");
            }
            return "[" + string.Join("; ", parts) + "]";
        }

        static IEnumerator SetupEggAndDispatchedStartCook(Fixture f, CaseResult item, string tag)
        {
            int eggInit = f.Session.Count("egg");
            int gasInit = f.Session.Gas;
            Need(f.Session.Setup.practice && f.Session.Lanes.Length == 2,
                tag + " expected MidgamePractice with 2 physical lanes, got practice=" + f.Session.Setup.practice + ", lanes=" + f.Session.Lanes.Length);

            bool eggRet = f.controller.UseIngredientAtLane(0, "egg");
            Need(eggRet, tag + " UseIngredientAtLane(0, egg) rejected: " + f.controller.InteractionHint);

            bool eggContact = false, idleAfterEgg = false;
            for (int frame = 0; frame < 40 * 60; frame++)
            {
                Tick(f);
                if (!eggContact && UnitsOf(f.Session.Lanes[0], "egg") == 1) eggContact = true;
                if (eggContact && f.controller.PendingWorkCount == 0 && !f.stage.WorkActorBusy && string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {
                    idleAfterEgg = true;
                    break;
                }
                if (frame % 60 == 0) yield return null;
            }
            Need(eggContact && idleAfterEgg && UnitsOf(f.Session.Lanes[0], "egg") == 1
                && f.Session.Lanes[0].step == KitchenStep.Prep && f.Session.Count("egg") == eggInit - 1,
                tag + " egg did not reach lane 0 Prep/idle: " + DescribeLaneState(f.Session, 0));

            bool methodRet = f.controller.SetFreeCookingMethod(0, FreeKitchenRules.MethodStirFry);
            Need(methodRet && f.Session.Lanes[0].freeBatch != null && f.Session.Lanes[0].freeBatch.method == FreeKitchenRules.MethodStirFry,
                tag + " SetFreeCookingMethod(0, stir_fry) failed: " + f.controller.InteractionHint);

            bool startRet = f.controller.UseCookware(0);
            Need(startRet, tag + " UseCookware(0) to dispatch start-cook rejected: " + f.controller.InteractionHint);
            Need(f.stage.WorkActorBusy && f.controller.ActiveWorkLabel.Contains("开火烹制")
                && f.controller.PendingWorkCount == 0 && f.Session.Lanes[0].step == KitchenStep.Prep
                && f.Session.Gas == gasInit && f.stage.WorkAudit != null && !f.stage.WorkAudit.contactCommitted,
                tag + " start-cook not in active uncommitted state: busy=" + f.stage.WorkActorBusy
                + ", active='" + f.controller.ActiveWorkLabel + "', pending=" + f.controller.PendingWorkCount
                + ", step=" + f.Session.Lanes[0].step + ", gas=" + f.Session.Gas + "/" + gasInit);

            item.detail += tag + " setup(egg=" + eggInit + "->" + f.Session.Count("egg")
                + ",gas=" + gasInit + ",active='" + f.controller.ActiveWorkLabel + "'," + DescribeLaneState(f.Session, 0) + ")";
        }

        // Case 1: 重排后取消 (defer-then-cancel)
        static IEnumerator DeferThenCancel(CaseResult item)
        {
            item.syntheticFixtureUsed = false;
            item.syntheticFixtureNote = "None; all actions use real public BusinessController/StreetStallStage entries on BusinessSetup.MidgamePractice().";

            Fixture f = Create(BusinessSetup.MidgamePractice());
            yield return null;

            int eggInit = f.Session.Count("egg");
            int riceInit = f.Session.Count("rice");
            int gasInit = f.Session.Gas;
            int elecInit = f.Session.Electricity;
            int coalInit = f.Session.Coal;
            int cartInit = f.Session.Cartridge;

            IEnumerator setup = SetupEggAndDispatchedStartCook(f, item, "[Case1:defer-then-cancel]");
            while (setup.MoveNext()) yield return setup.Current;

            int historyBeforeRice = f.controller.CaptureWorkHistory().Length;
            int riceBefore = f.Session.Count("rice");
            int gasBefore = f.Session.Gas;

            // Append legal rice while start-cook is active and not yet contacted -> defers start-cook into pendingWork.
            bool riceRet = f.controller.UseIngredientAtLane(0, "rice");
            string activeAfterRice = f.controller.ActiveWorkLabel ?? "";
            string pendingAfterRice = f.controller.PendingWorkLabel ?? "";
            int pendingCountAfterRice = f.controller.PendingWorkCount;
            bool busyAfterRice = f.stage.WorkActorBusy;
            int riceAfterCall = f.Session.Count("rice");
            int gasAfterCall = f.Session.Gas;

            item.detail += " | afterRice(ret=" + riceRet + ",busy=" + busyAfterRice
                + ",active='" + activeAfterRice + "',pending='" + pendingAfterRice + "',pCount=" + pendingCountAfterRice
                + ",rice=" + riceAfterCall + ",gas=" + gasAfterCall + ",hint='" + (f.controller.InteractionHint ?? "") + "')";

            Need(riceRet, "UseIngredientAtLane(0, rice) while start-cook uncommitted was rejected: " + item.detail);
            Need(busyAfterRice && activeAfterRice.Contains("投料") && pendingCountAfterRice == 1 && pendingAfterRice.Contains("开火烹制"),
                "Expected in-flight rice active and deferred start-cook pending: " + item.detail);
            Need(riceAfterCall == riceBefore && gasAfterCall == gasBefore && f.Session.Lanes[0].step == KitchenStep.Prep,
                "Premature stock/gas/step mutation right after deferring start-cook: " + item.detail);

            // Real CancelPendingWork: clears deferred start-cook, leaves in-flight rice active.
            f.controller.CancelPendingWork();
            int pendingAfterCancel = f.controller.PendingWorkCount;
            string pendingLabelAfterCancel = f.controller.PendingWorkLabel ?? "";
            string activeAfterCancel = f.controller.ActiveWorkLabel ?? "";
            bool busyAfterCancel = f.stage.WorkActorBusy;
            bool hasCookwareAfterCancel = f.controller.HasPendingIntent(StallInteractionKind.Cookware, 0);

            item.detail += " | afterCancel(busy=" + busyAfterCancel + ",active='" + activeAfterCancel
                + "',pending='" + pendingLabelAfterCancel + "',pCount=" + pendingAfterCancel
                + ",hasCookwareIntent=" + hasCookwareAfterCancel + ",rice=" + f.Session.Count("rice")
                + ",gas=" + f.Session.Gas + ",hint='" + (f.controller.InteractionHint ?? "") + "')";

            Need(pendingAfterCancel == 0 && string.IsNullOrEmpty(pendingLabelAfterCancel) && !hasCookwareAfterCancel,
                "CancelPendingWork did not clear deferred start-cook: " + item.detail);
            Need(busyAfterCancel && activeAfterCancel.Contains("投料"),
                "CancelPendingWork unexpectedly stopped in-flight rice: " + item.detail);
            Need(f.Session.Count("rice") == riceBefore && f.Session.Gas == gasBefore,
                "CancelPendingWork mutated rice or gas before contact: " + item.detail);

            // Wait frame-by-frame for in-flight rice to contact and actor to become idle.
            bool riceContacted = false, idleAfterRice = false;
            float riceContactAt = -1f;
            for (int frame = 0; frame < 40 * 60; frame++)
            {
                Tick(f);
                if (!riceContacted && UnitsOf(f.Session.Lanes[0], "rice") == 1)
                {
                    riceContacted = true;
                    riceContactAt = f.Session.Elapsed;
                }
                if (riceContacted && f.controller.PendingWorkCount == 0 && !f.stage.WorkActorBusy && string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {
                    idleAfterRice = true;
                    break;
                }
                if (frame % 60 == 0) yield return null;
            }
            Need(riceContacted && idleAfterRice, "In-flight rice did not contact and settle to idle within 40s: " + item.detail);

            // Advance 180 extra frames (3.0s) to prove deferred start-cook does NOT silently restart.
            for (int extra = 0; extra < 180; extra++)
            {
                Tick(f);
                if (extra % 60 == 0) yield return null;
            }

            BusinessWorkAudit[] history = f.controller.CaptureWorkHistory();
            int cancelledStartCount = 0, committedStartCount = 0, committedRiceCount = 0;
            for (int i = historyBeforeRice; i < history.Length; i++)
            {
                var w = history[i];
                if (w.action == "开火烹制")
                {
                    if (w.status == "cancelled_pending" && w.contactAt < 0) cancelledStartCount++;
                    if (w.status == "committed") committedStartCount++;
                }
                else if (w.action == "投料" && w.lane == 0 && w.status == "committed" && w.contactAt >= 0)
                {
                    committedRiceCount++;
                }
            }

            item.detail += " | final(riceContactAt=" + riceContactAt.ToString("F3")
                + ",egg=" + eggInit + "->" + f.Session.Count("egg")
                + ",rice=" + riceInit + "->" + f.Session.Count("rice")
                + ",gas=" + gasInit + "->" + f.Session.Gas
                + ",elec=" + elecInit + "->" + f.Session.Electricity
                + ",coal=" + coalInit + "->" + f.Session.Coal
                + ",cart=" + cartInit + "->" + f.Session.Cartridge
                + ",busy=" + f.stage.WorkActorBusy + ",pCount=" + f.controller.PendingWorkCount
                + ",active='" + (f.controller.ActiveWorkLabel ?? "") + "'"
                + "," + DescribeLaneState(f.Session, 0) + "," + DescribeLaneState(f.Session, 1)
                + ",counts(cancelledStart=" + cancelledStartCount + ",committedStart=" + committedStartCount + ",committedRice=" + committedRiceCount + ")"
                + ",work=" + FormatWorkSlice(history, historyBeforeRice) + ")";

            Need(UnitsOf(f.Session.Lanes[0], "egg") == 1 && UnitsOf(f.Session.Lanes[0], "rice") == 1,
                "Lane 0 must hold egg=1 and rice=1 after in-flight rice contact: " + item.detail);
            Need(f.Session.Count("rice") == riceBefore - 1,
                "In-flight rice must debit exactly 1 unit: " + item.detail);
            Need(f.Session.Count("egg") == eggInit - 1,
                "Egg count drifted: " + item.detail);
            Need(f.Session.Gas == gasBefore && f.Session.Electricity == elecInit && f.Session.Coal == coalInit && f.Session.Cartridge == cartInit,
                "Cancelled deferred start-cook unexpectedly debited fuel: " + item.detail);
            Need(f.Session.Lanes[0].step == KitchenStep.Prep,
                "Lane 0 silently left Prep (possible silent restart of cancelled start-cook): " + item.detail);
            Need(f.controller.PendingWorkCount == 0 && !f.stage.WorkActorBusy && string.IsNullOrEmpty(f.controller.ActiveWorkLabel),
                "Controller/actor not idle after settle + 3s hold: " + item.detail);
            Need(cancelledStartCount == 1 && committedStartCount == 0 && committedRiceCount == 1,
                "Work history mismatch for defer-then-cancel: " + item.detail);
        }

        // Case 2: 队列容量 (queue-capacity-guard)
        static IEnumerator QueueCapacityGuard(CaseResult item)
        {
            item.syntheticFixtureUsed = true;
            item.syntheticFixtureNote = "SYNTHETIC CAPACITY FIXTURE (NOT player operations): 7 inert placeholder WorkIntent items (kind=Serving, lane=1, step=Pack, batchId='', freeInput=false, freeStart=false, reservation=false) were injected via reflection into private BusinessController.pendingWork to reach pendingWork.Count==7 against PendingWorkCapacity==8 while lane 0 holds an active uncommitted start-cook. 附加 V01/U01 用于双锅容量夹具";

            BusinessSetup fixtureSetup = BusinessSetup.MidgamePractice();
            fixtureSetup.equipment.Add("V01");
            fixtureSetup.equipment.Add("U01");
            Fixture f = Create(fixtureSetup);
            yield return null;

            int eggInit = f.Session.Count("egg");
            int riceInit = f.Session.Count("rice");
            int gasInit = f.Session.Gas;
            int elecInit = f.Session.Electricity;
            int coalInit = f.Session.Coal;
            int cartInit = f.Session.Cartridge;

            IEnumerator setup = SetupEggAndDispatchedStartCook(f, item, "[Case2:queue-capacity-guard]");
            while (setup.MoveNext()) yield return setup.Current;

            int capacity = f.controller.PendingWorkCapacity;
            string lane1Reason = "";
            bool lane1AvailableForRice = f.Session.Lanes[1].step == KitchenStep.Idle
                && f.Session.CanAddFreeIngredient(1, "rice", out lane1Reason);
            Need(capacity == 8, "Expected PendingWorkCapacity==8 on 2-lane MidgamePractice, got " + capacity);
            Need(lane1AvailableForRice, "Expected lane 1 (2号锅) available and Idle: " + DescribeLaneState(f.Session, 1) + " reason=" + lane1Reason);

            int historyBeforeInject = f.controller.CaptureWorkHistory().Length;
            int riceBefore = f.Session.Count("rice");
            int gasBefore = f.Session.Gas;

            // Inject 7 synthetic inert placeholders into private pendingWork via reflection.
            int injectedCount = InjectSyntheticPlaceholders(f.controller, f.Session.Elapsed, 7);
            Need(injectedCount == 7 && f.controller.PendingWorkCount == 7,
                "Failed to inject 7 synthetic placeholders into pendingWork (injected=" + injectedCount + ", count=" + f.controller.PendingWorkCount + ")");

            item.detail += " | [SYNTHETIC_FIXTURE_NOT_PLAYER_OP: injected " + injectedCount
                + " inert placeholder WorkIntents into pendingWork via reflection; pCount=" + f.controller.PendingWorkCount
                + "/" + capacity + ",lane1Available=" + lane1AvailableForRice + "]";

            // Real UseIngredientAtLane(0, "rice") needs 2 slots (rice + deferred start-cook): 7 + 2 > 8 -> must reject immediately.
            bool riceRet = f.controller.UseIngredientAtLane(0, "rice");
            string hintAfterReject = f.controller.InteractionHint ?? "";
            bool busyAfterReject = f.stage.WorkActorBusy;
            string activeAfterReject = f.controller.ActiveWorkLabel ?? "";
            int pendingAfterReject = f.controller.PendingWorkCount;
            bool hasCookwareAfterReject = f.controller.HasPendingIntent(StallInteractionKind.Cookware, 0);

            item.detail += " | afterReject(ret=" + riceRet + ",hint='" + hintAfterReject
                + "',busy=" + busyAfterReject + ",active='" + activeAfterReject + "',pCount=" + pendingAfterReject
                + ",hasCookwareIntent=" + hasCookwareAfterReject + ",rice=" + f.Session.Count("rice")
                + ",gas=" + f.Session.Gas + ",lane0Step=" + f.Session.Lanes[0].step + ",lane1Step=" + f.Session.Lanes[1].step + ")";

            Need(!riceRet, "UseIngredientAtLane(0, rice) must be rejected immediately when pendingWork.Count==7, capacity==8, and defer requires 2 slots: " + item.detail);
            Need(busyAfterReject && activeAfterReject.Contains("开火烹制") && hasCookwareAfterReject,
                "Original active start-cook was lost or interrupted by rejected rice call: " + item.detail);
            Need(pendingAfterReject == 7,
                "Rejected rice call mutated pendingWork.Count (expected 7, got " + pendingAfterReject + "): " + item.detail);
            Need(f.Session.Count("rice") == riceBefore && f.Session.Count("egg") == eggInit - 1 && f.Session.Gas == gasBefore,
                "Rejected rice call prematurely debited stock or gas: " + item.detail);
            Need(f.Session.Lanes[0].step == KitchenStep.Prep && UnitsOf(f.Session.Lanes[0], "rice") == 0 && f.Session.Lanes[1].step == KitchenStep.Idle,
                "Rejected rice call mutated lane 0 or lane 1 state: " + item.detail);

            // Real CancelPendingWork clears the 7 synthetic placeholders while preserving active start-cook.
            f.controller.CancelPendingWork();
            int pendingAfterCancel = f.controller.PendingWorkCount;
            bool busyAfterCancel = f.stage.WorkActorBusy;
            string activeAfterCancel = f.controller.ActiveWorkLabel ?? "";

            item.detail += " | afterCancel(pCount=" + pendingAfterCancel + ",busy=" + busyAfterCancel
                + ",active='" + activeAfterCancel + "',rice=" + f.Session.Count("rice") + ",gas=" + f.Session.Gas + ")";

            Need(pendingAfterCancel == 0, "CancelPendingWork did not clear the 7 synthetic placeholders: " + item.detail);
            Need(busyAfterCancel && activeAfterCancel.Contains("开火烹制"),
                "CancelPendingWork unexpectedly cleared active start-cook: " + item.detail);
            Need(f.Session.Count("rice") == riceBefore && f.Session.Gas == gasBefore,
                "CancelPendingWork mutated stock or gas before start-cook contact: " + item.detail);

            // Real frame-by-frame ticks until original start-cook contacts and actor finishes.
            bool startContacted = false, actorFinished = false;
            float startContactAt = -1f;
            for (int frame = 0; frame < 40 * 60; frame++)
            {
                Tick(f);
                if (!startContacted && f.Session.Lanes[0].step != KitchenStep.Prep)
                {
                    startContacted = true;
                    startContactAt = f.Session.Elapsed;
                }
                if (startContacted && f.controller.PendingWorkCount == 0 && !f.stage.WorkActorBusy && string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {
                    actorFinished = true;
                    break;
                }
                if (frame % 60 == 0) yield return null;
            }
            Need(startContacted && actorFinished,
                "Original active start-cook did not contact and finish within 40s: " + item.detail);

            BusinessWorkAudit[] history = f.controller.CaptureWorkHistory();
            int cancelledSynthetic = 0, committedStart = 0, anyRiceWork = 0;
            for (int i = historyBeforeInject; i < history.Length; i++)
            {
                var w = history[i];
                if (w.action != null && w.action.StartsWith("合成占位#", StringComparison.Ordinal) && w.status == "cancelled_pending")
                    cancelledSynthetic++;
                else if (w.action == "开火烹制" && w.lane == 0 && w.status == "committed" && w.contactAt >= 0)
                    committedStart++;
                else if (w.action == "投料")
                    anyRiceWork++;
            }

            item.detail += " | final(startContactAt=" + startContactAt.ToString("F3")
                + ",egg=" + eggInit + "->" + f.Session.Count("egg")
                + ",rice=" + riceInit + "->" + f.Session.Count("rice")
                + ",gas=" + gasInit + "->" + f.Session.Gas
                + ",elec=" + elecInit + "->" + f.Session.Electricity
                + ",coal=" + coalInit + "->" + f.Session.Coal
                + ",cart=" + cartInit + "->" + f.Session.Cartridge
                + "," + DescribeLaneState(f.Session, 0) + "," + DescribeLaneState(f.Session, 1)
                + ",counts(cancelledSynthetic=" + cancelledSynthetic + ",committedStart=" + committedStart + ",anyRiceWork=" + anyRiceWork + ")"
                + ",work=" + FormatWorkSlice(history, historyBeforeInject) + ")";

            Need(f.Session.Gas == gasBefore - 1,
                "Original start-cook must debit exactly 1 gas unit: " + item.detail);
            Need(f.Session.Electricity == elecInit && f.Session.Coal == coalInit && f.Session.Cartridge == cartInit,
                "Unexpected non-gas energy debit: " + item.detail);
            Need(f.Session.Count("rice") == riceBefore && UnitsOf(f.Session.Lanes[0], "rice") == 0,
                "Rejected rice must not debit inventory or enter lane 0: " + item.detail);
            Need(UnitsOf(f.Session.Lanes[0], "egg") == 1 && f.Session.Count("egg") == eggInit - 1,
                "Egg state drifted after start-cook contact: " + item.detail);
            Need(f.Session.Lanes[0].step == KitchenStep.Heating || f.Session.Lanes[0].step == KitchenStep.Utensil,
                "Lane 0 did not enter active cooking step after start-cook contact: " + item.detail);
            Need(cancelledSynthetic == 7 && committedStart == 1 && anyRiceWork == 0,
                "Work history mismatch for queue-capacity-guard: " + item.detail);
        }

        static int InjectSyntheticPlaceholders(BusinessController controller, float queuedAt, int count)
        {
            FieldInfo pendingField = typeof(BusinessController).GetField("pendingWork", BindingFlags.Instance | BindingFlags.NonPublic);
            Need(pendingField != null, "Reflection failed: BusinessController.pendingWork field not found");
            IList pendingList = pendingField.GetValue(controller) as IList;
            Need(pendingList != null, "Reflection failed: BusinessController.pendingWork is not an IList");
            Type intentType = typeof(BusinessController).GetNestedType("WorkIntent", BindingFlags.NonPublic);
            Need(intentType != null, "Reflection failed: BusinessController.WorkIntent nested type not found");

            for (int i = 0; i < count; i++)
            {
                object intent = Activator.CreateInstance(intentType, true);
                SetField(intentType, intent, "id", 9001 + i);
                SetField(intentType, intent, "lane", 1);
                SetField(intentType, intent, "order", -1);
                SetField(intentType, intent, "kind", StallInteractionKind.Serving);
                SetField(intentType, intent, "step", KitchenStep.Pack);
                SetField(intentType, intent, "item", "");
                SetField(intentType, intent, "label", "合成占位#" + (i + 1));
                SetField(intentType, intent, "note", "synthetic-capacity-placeholder");
                SetField(intentType, intent, "reservation", false);
                SetField(intentType, intent, "committed", false);
                SetField(intentType, intent, "queuedAt", queuedAt);
                SetField(intentType, intent, "startedAt", -1f);
                SetField(intentType, intent, "contactAt", -1f);
                SetField(intentType, intent, "pathStart", 0f);
                SetField(intentType, intent, "freeInput", false);
                SetField(intentType, intent, "freeStart", false);
                SetField(intentType, intent, "counterMeal", "");
                SetField(intentType, intent, "batchId", "");
                SetField(intentType, intent, "productId", "");
                SetField(intentType, intent, "placeAfterPack", false);
                pendingList.Add(intent);
            }
            return count;
        }

        static void SetField(Type type, object instance, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Need(field != null, "Reflection failed: field '" + fieldName + "' not found on " + type.FullName);
            field.SetValue(instance, value);
        }

        static void DisposeFixture()
        {
            if (live == null) return;
            var f = live;
            live = null;
            try { f.stage?.Release(); } catch (Exception ex) { errors.Add("DisposeFixture stage.Release: " + ex); }
            if (f.root) UnityEngine.Object.DestroyImmediate(f.root);
        }

        static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void Log(string condition, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(condition + "\n" + trace);
        }

        static void Finish(string failure)
        {
            DisposeFixture();
            Application.logMessageReceived -= Log;
            report.failure = failure ?? "";
            report.passed = 0;
            report.failed = 0;
            foreach (CaseResult item in report.cases)
            {
                if (item.passed) report.passed++;
                else report.failed++;
            }
            report.engineErrors = errors.ToArray();
            report.success = string.IsNullOrEmpty(report.failure)
                && report.failed == 0
                && report.passed == ExpectedCaseCount
                && errors.Count == 0;
            if (!string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            }
            EditorApplication.Exit(report.success ? 0 : 1);
        }
    }
}
