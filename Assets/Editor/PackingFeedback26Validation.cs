using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Static CartAI vehicle-packing solver and user-facing feedback validation only.
    // Does not disable colliders, fabricate PackingPlan results, process payment,
    // or claim human gameplay / UI interaction acceptance.
    public static class PackingFeedback26Validation
    {
        private const string OutputArg = "--packing-feedback-output";
        private const string SearchBudgetPhrase = "暂时没找到装车方案";
        private const string OverweightPhrase = "100 kg 承重";
        private const string NoSupportedLayoutPhrase = "没有适合放置";

        [Serializable]
        public sealed class CaseResult
        {
            public string name = "";
            public bool passed;
            public bool fullyValidated;
            public bool planSuccess;
            public bool searchExhausted;
            public string failureCode = "";
            public string planReason = "";
            public string feedback = "";
            public float knownWeightKg;
            public int searchedCandidates;
            public double elapsedMs;
            public string[] equipment = Array.Empty<string>();
            public string[] cargo = Array.Empty<string>();
            public string[] packages = Array.Empty<string>();
            public string exception = "";
        }

        [Serializable]
        public sealed class ValidationReport
        {
            public bool success;
            public string scope = "Static CartAI VehicleLoadout/VehiclePacking model and user-facing feedback checks only; no collider disabling, fabricated PackingPlan, payment, UI, or human-operation claim.";
            public string outputPath = "";
            public string fixtureException = "";
            public CaseResult[] cases = Array.Empty<CaseResult>();
        }

        public static void Run()
        {
            if (!TryResolveNewOutputPath(Environment.GetCommandLineArgs(), out string output, out Exception pathError))
            {
                Debug.LogError("PACKING_FEEDBACK_26_ERROR " + pathError.Message);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }
                throw pathError;
            }

            var report = new ValidationReport { outputPath = output };
            var results = new List<CaseResult>();
            GameObject fixture = null;

            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                MethodInfo makeFoodMethod = typeof(WorldRuntime).GetMethod(
                    "MakeFood",
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string), typeof(Transform), typeof(float) },
                    null);
                Need(makeFoodMethod != null, "Missing private static WorldRuntime.MakeFood(string, Transform, float).");

                GameObject cartPrefab = Resources.Load<GameObject>("Models/CartAI");
                Need(cartPrefab != null, "Missing real CartAI prefab at Resources/Models/CartAI.");

                fixture = new GameObject("PackingFeedback26ValidationFixture");
                var cartRoot = new GameObject("CartAI");
                cartRoot.transform.SetParent(fixture.transform, false);
                UnityEngine.Object.Instantiate(cartPrefab, cartRoot.transform, false);

                VehicleLoadout loadout = cartRoot.AddComponent<VehicleLoadout>();
                loadout.Initialize(cartRoot.transform, (id, parent) => CreateVisual(id, parent, makeFoodMethod));

                CaseResult case1 = RunSearchBudgetExceededCase(loadout);
                results.Add(case1);

                CaseResult case2 = RunOverweightCase(loadout, case1.feedback);
                results.Add(case2);

                CaseResult case3 = RunNoSupportedLayoutCase(loadout);
                results.Add(case3);
            }
            catch (Exception ex)
            {
                report.fixtureException = ex.ToString();
            }
            finally
            {
                if (fixture != null)
                {
                    UnityEngine.Object.DestroyImmediate(fixture);
                }
            }

            report.cases = results.ToArray();
            report.success = string.IsNullOrEmpty(report.fixtureException)
                && results.Count == 3
                && results.All(c => c.passed);

            WriteNewJsonReport(output, report);
            Debug.Log("PACKING_FEEDBACK_26 " + (report.success ? "PASS " : "FAIL ") + output);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.success ? 0 : 1);
            }
        }

        private static CaseResult RunSearchBudgetExceededCase(VehicleLoadout loadout)
        {
            var result = new CaseResult { name = "search_budget_exceeded_18x_rice_small" };
            try
            {
                var run = new RunState(.25f, new RunProfile
                {
                    careerId = "career1",
                    selectedTraits = Array.Empty<string>()
                });
                IReadOnlyList<string> equipment = run.LoadedEquipment;
                string[] packages = run.PackingSupplies.ToArray();
                List<string> cargo = Enumerable.Repeat("rice_small", 18).ToList();

                result.equipment = equipment.ToArray();
                result.cargo = cargo.ToArray();
                result.packages = packages;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                string feedback = loadout.Validate(equipment, cargo, packages);
                stopwatch.Stop();

                PackingPlan plan = loadout.Plan;
                PopulateMetrics(result, plan, feedback, stopwatch.Elapsed.TotalMilliseconds);

                Need(plan != null, "VehicleLoadout.Plan was null after Validate.");
                Need(!plan.FullyValidated && !plan.success, "Expected 18x rice_small validation to fail.");
                Need(plan.failureCode == "SearchBudgetExceeded",
                    "Expected Plan.failureCode==SearchBudgetExceeded, actual: " + plan.failureCode + " (" + plan.reason + ")");
                Need(plan.searchExhausted, "Expected Plan.searchExhausted==true.");
                Need(plan.searchedCandidates > 0, "Expected searchedCandidates > 0, actual: " + plan.searchedCandidates);
                Need(!string.IsNullOrEmpty(feedback), "Expected non-empty feedback text.");
                Need(feedback.Contains(SearchBudgetPhrase),
                    "Expected feedback to contain '" + SearchBudgetPhrase + "', actual: " + feedback);
                Need(!ClaimsSpaceFull(feedback),
                    "Search-budget feedback must not claim vehicle space is full, actual: " + feedback);

                result.passed = true;
            }
            catch (Exception ex)
            {
                result.passed = false;
                result.exception = ex.ToString();
            }
            return result;
        }

        private static CaseResult RunOverweightCase(VehicleLoadout loadout, string searchBudgetFeedback)
        {
            var result = new CaseResult { name = "overweight_real_meat_over_100kg" };
            try
            {
                float unitWeightKg = RuntimeItemBalance.WeightKg("meat");
                Need(unitWeightKg > 0f, "Expected positive weight for meat, actual: " + unitWeightKg);

                int count = Mathf.FloorToInt(100f / unitWeightKg) + 1;
                string[] equipment = Array.Empty<string>();
                string[] packages = Array.Empty<string>();
                List<string> cargo = Enumerable.Repeat("meat", count).ToList();

                result.equipment = equipment;
                result.cargo = cargo.ToArray();
                result.packages = packages;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                string feedback = loadout.Validate(equipment, cargo, packages);
                stopwatch.Stop();

                PackingPlan plan = loadout.Plan;
                PopulateMetrics(result, plan, feedback, stopwatch.Elapsed.TotalMilliseconds);

                Need(plan != null, "VehicleLoadout.Plan was null after Validate.");
                Need(!plan.FullyValidated && !plan.success, "Expected overweight cargo validation to fail.");
                Need(plan.knownWeightKg > 100f,
                    "Expected knownWeightKg > 100 kg, actual: " + plan.knownWeightKg.ToString("0.##"));
                Need(plan.failureCode == "Overweight",
                    "Expected Plan.failureCode==Overweight, actual: " + plan.failureCode + " (" + plan.reason + ")");
                Need(!plan.searchExhausted, "Expected Overweight failure not to set searchExhausted=true.");
                Need(!string.IsNullOrEmpty(feedback), "Expected non-empty overweight feedback.");
                Need(feedback.Contains(OverweightPhrase),
                    "Expected feedback to contain '" + OverweightPhrase + "', actual: " + feedback);
                Need(!feedback.Contains(SearchBudgetPhrase),
                    "Overweight feedback must not reuse search-budget prompt, actual: " + feedback);
                if (!string.IsNullOrEmpty(searchBudgetFeedback))
                {
                    Need(!string.Equals(feedback, searchBudgetFeedback, StringComparison.Ordinal),
                        "Overweight feedback must not equal search-budget feedback.");
                }

                result.passed = true;
            }
            catch (Exception ex)
            {
                result.passed = false;
                result.exception = ex.ToString();
            }
            return result;
        }

        private static CaseResult RunNoSupportedLayoutCase(VehicleLoadout loadout)
        {
            var result = new CaseResult { name = "no_supported_layout_career2_real_ricebucket" };
            try
            {
                CareerDefinition career = CharacterCatalog.Data.careers.First(c => c.id == "career2");
                MethodInfo profileMethod = typeof(V10PackingValidation).GetMethod("Profile", BindingFlags.Static | BindingFlags.NonPublic);
                Need(profileMethod != null, "Missing actual career profile builder");
                var run = new RunState(4, (RunProfile)profileMethod.Invoke(null, new object[] { career }));
                run.LoadValidator = (equipment, cargo) => loadout.Validate(equipment, cargo, run.PackingSupplies);
                while (run.HasUnloadedEquipment)
                    Need(run.Act("StovePickup", true) && run.Act("HomeCart", true), "Initial loading failed: " + run.Message);
                Need(run.Act("HomeExit", true), "Could not depart home");
                Need(SupplyCatalog.IsCounterItem("RiceBucket"), "Rice bucket is not actually sold");
                Need(run.AddSupply("RiceBucket"), "Actual basket rejected rice bucket: " + run.Message);
                Need(run.CartTotalCents <= run.CashCents, "Cannot afford actual selection");
                result.equipment = run.LoadedEquipment.ToArray();
                result.cargo = run.Cart.ToArray();
                result.packages = run.PackingSupplies.ToArray();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                bool preview = run.PreviewCheckoutSelection(run.Cart.ToArray(), out int total, out string feedback);
                stopwatch.Stop();
                PackingPlan plan = loadout.Plan;
                PopulateMetrics(result, plan, feedback, stopwatch.Elapsed.TotalMilliseconds);
                Need(!preview && plan != null, "Expected actual checkout preview to reject placement");
                Need(plan.failureCode == "NoSupportedLayout", "Expected NoSupportedLayout, actual: " + plan.failureCode);
                Need(!plan.searchExhausted, "Actual layout search exhausted its budget");
                Need(plan.knownWeightKg <= 100f, "Actual cargo exceeded the known 100 kg vehicle limit");
                Need(!string.IsNullOrEmpty(feedback) && feedback.Contains(NoSupportedLayoutPhrase), "Missing layout-specific feedback: " + feedback);
                Need(!feedback.Contains(SearchBudgetPhrase) && !feedback.Contains(OverweightPhrase), "Wrong failure feedback: " + feedback);
                result.passed = true;
            }
            catch (Exception ex) { result.passed = false; result.exception = ex.ToString(); }
            return result;
        }

        private static GameObject CreateVisual(string id, Transform parent, MethodInfo makeFoodMethod)
        {
            Need(!string.IsNullOrEmpty(id), "Item id cannot be null or empty.");
            GameObject created = IsEquipmentOrSupplyVisual(id)
                ? EquipmentVisualFactory.Create(id, parent)
                : (GameObject)makeFoodMethod.Invoke(null, new object[] { id, parent, .36f });
            Need(created != null, "Real visual factory returned null for item: " + id);
            return created;
        }

        private static bool IsEquipmentOrSupplyVisual(string id)
        {
            return id == "RiceBucket"
                || id.StartsWith("B", StringComparison.Ordinal)
                || id.StartsWith("V", StringComparison.Ordinal)
                || id.StartsWith("U", StringComparison.Ordinal)
                || id.StartsWith("E", StringComparison.Ordinal)
                || id.StartsWith("P", StringComparison.Ordinal)
                || id.StartsWith("S", StringComparison.Ordinal);
        }

        private static bool ClaimsSpaceFull(string feedback)
        {
            if (string.IsNullOrEmpty(feedback)) return false;
            return feedback.Contains(NoSupportedLayoutPhrase)
                || feedback.Contains("空位")
                || feedback.Contains("空间满")
                || feedback.Contains("空间不足")
                || feedback.Contains("已满")
                || feedback.Contains("装满")
                || feedback.Contains("放不下");
        }

        private static void PopulateMetrics(CaseResult result, PackingPlan plan, string feedback, double elapsedMs)
        {
            result.feedback = feedback ?? "";
            result.elapsedMs = Math.Round(elapsedMs, 3);
            if (plan == null) return;
            result.fullyValidated = plan.FullyValidated;
            result.planSuccess = plan.success;
            result.searchExhausted = plan.searchExhausted;
            result.failureCode = plan.failureCode ?? "";
            result.planReason = plan.reason ?? "";
            result.knownWeightKg = plan.knownWeightKg;
            result.searchedCandidates = plan.searchedCandidates;
        }

        private static bool TryResolveNewOutputPath(string[] args, out string output, out Exception error)
        {
            output = "";
            error = null;
            int at = args == null ? -1 : Array.IndexOf(args, OutputArg);
            if (at < 0 || at + 1 >= args.Length || string.IsNullOrWhiteSpace(args[at + 1]))
            {
                error = new ArgumentException("Output path required via " + OutputArg + " <path>.");
                return false;
            }
            string candidate = Path.GetFullPath(args[at + 1]);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                error = new IOException("Refusing to overwrite existing evidence path: " + candidate);
                return false;
            }
            output = candidate;
            return true;
        }

        private static void WriteNewJsonReport(string output, ValidationReport report)
        {
            string directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(JsonUtility.ToJson(report, true));
            }
        }

        private static void Need(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
