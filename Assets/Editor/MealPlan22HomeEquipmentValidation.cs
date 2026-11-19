using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Guochao
{
    // Static projection checks only: genuine MealPlan/BusinessSession rules,
    // but no world interaction, transport, or manual game input is simulated.
    public static class MealPlan22HomeEquipmentValidation
    {
        [Serializable] sealed class Report
        {
            public bool pass;
            public string scope = "Read-only meal-plan projection with explicit home/loaded fixtures; not a full transport playthrough.";
            public string[] checks = Array.Empty<string>();
            public string error = "";
        }

        static readonly List<string> checks = new List<string>();
        static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }

        static RunState Fixture(string[] home, string[] loaded, bool tool = true, bool batteryLoaded = true, int electricity = 45)
        {
            var state = new RunState();
            state.HomeEquipment.Clear();
            state.HomeEquipment.AddRange(home);
            state.LoadedEquipment.Clear();
            state.LoadedEquipment.AddRange(loaded);
            state.LoadedEquipment.Add("RiceBucket");
            if (tool) state.LoadedEquipment.Add("U14");
            if (batteryLoaded) state.LoadedEquipment.Add("E05");
            state.Supplies["rice"] = 10;
            state.Supplies["prepared"] = 10;
            state.Supplies["P05"] = 10;
            var data = state.CaptureSave();
            data.electricity = electricity;
            data.stoveLoaded = Array.IndexOf(loaded, "B04") >= 0;
            var restored = RunState.Restore(data);
            Need(restored.SetMealPlan("prepared_rice", 2), "Set prepared-rice plan");
            return restored;
        }

        static MealPlanLine Line(MealPlanReport report, string id)
            => report.lines.Find(line => line.key == id);

        static int RecipeIndex(string id)
            => Array.FindIndex(BusinessSession.Recipes, recipe => recipe.id == id);

        // Reflection keeps the same validation compilable against the old
        // source for a meaningful pre-fix failure, before the new field exists.
        static int AtHome(MealPlanLine line)
        {
            var field = typeof(MealPlanLine).GetField("atHome");
            Need(field != null, "Energy line exposes its physically at-home subset");
            return (int)field.GetValue(line);
        }

        static void HomeMicrowaveIsDisclosed()
        {
            foreach (string microwave in new[] { "B07", "B08" })
            {
                var run = Fixture(new[] { microwave }, Array.Empty<string>());
                string before = JsonUtility.ToJson(run.CaptureSave());
                var report = MealPlan.Build(run);
                var line = Line(report, microwave);
                Need(line != null && line.channel == "home" && line.owned == 1 && line.missing == 0 && line.estimatedCents == 0,
                    microwave + " at home is shown to load, not offered as a purchase");
                Need(!report.cookableNow.Contains(RecipeIndex("prepared_rice")),
                    microwave + " still at home does not become loaded/cookable");
                Need(JsonUtility.ToJson(run.CaptureSave()) == before,
                    microwave + " plan projection does not move items or alter the save");
            }
        }

        static void OneOfTwoHomeMicrowavesIsEnough()
        {
            var run = Fixture(new[] { "B07", "B08" }, Array.Empty<string>());
            var report = MealPlan.Build(run);
            var b07 = Line(report, "B07"); var b08 = Line(report, "B08");
            Need((b07 != null ? 1 : 0) + (b08 != null ? 1 : 0) == 1,
                "Two compatible microwaves at home need only one load reminder");
            var chosen = b07 ?? b08;
            Need(chosen.channel == "home" && chosen.missing == 0 && chosen.estimatedCents == 0,
                "The suggested home microwave carries no purchase charge");
        }

        static void LoadedAlternativeDoesNotRequireHomeBackup()
        {
            // Missing U14 forces a tool recommendation while B08 already meets
            // the cooking-base requirement. This catches the previous broad
            // 'if anything is missing, list every related home device' logic.
            var run = Fixture(new[] { "B07" }, new[] { "B08" }, false);
            var report = MealPlan.Build(run);
            Need(Line(report, "B07") == null,
                "Loaded B08 makes the home B07 optional even when a tool is missing");
            var tool = Line(report, "U14");
            Need(tool != null && tool.missing == 1 && tool.estimatedCents == run.PriceCents("U14"),
                "Genuinely missing U14 remains an optional-plan purchase suggestion");
        }

        static void OrdinaryStoveCannotReplaceMicrowave()
        {
            var run = Fixture(new[] { "B07" }, new[] { "B04" });
            var report = MealPlan.Build(run);
            Need(Line(report, "B07") != null && Line(report, "B07").channel == "home",
                "Loaded B04 cannot substitute the home microwave for reheating");
            Need(!report.cookableNow.Contains(RecipeIndex("prepared_rice")),
                "B04 alone does not make the reheat recipe cookable");
        }

        static void LoadedMicrowaveIsCookable()
        {
            var run = Fixture(Array.Empty<string>(), new[] { "B07" });
            var report = MealPlan.Build(run);
            Need(report.cookableNow.Contains(RecipeIndex("prepared_rice")),
                "Loaded B07 plus food, packaging, tool, battery and power is cookable");
            Need(Line(report, "B07") == null || Line(report, "B07").channel != "home",
                "A loaded microwave never receives a home-only reminder");
        }

        static void BatteryLocationAndShortage()
        {
            var home = Fixture(new[] { "E05" }, new[] { "B07" }, batteryLoaded: false);
            string before = JsonUtility.ToJson(home.CaptureSave());
            var report = MealPlan.Build(home);
            var battery = Line(report, "electricity");
            Need(battery != null && battery.owned == 45 && AtHome(battery) == 45 && battery.missing == 0,
                "Home battery reports its real 45 points without adding another 45");
            Need(battery.note.Contains("电瓶在家") && battery.note.Contains("装车"),
                "Home battery energy is explicitly marked as needing loading");
            Need(!report.cookableNow.Contains(RecipeIndex("prepared_rice")),
                "A battery at home does not power the already-loaded microwave");
            Need(battery.estimatedCents == 0 && JsonUtility.ToJson(home.CaptureSave()) == before,
                "Home battery is not rebought or moved by a plan preview");

            var loaded = Fixture(Array.Empty<string>(), new[] { "B07" });
            var loadedReport = MealPlan.Build(loaded);
            var loadedBattery = Line(loadedReport, "electricity");
            Need(loadedBattery != null && AtHome(loadedBattery) == 0 && !loadedBattery.note.Contains("电瓶在家"),
                "Loaded battery is not called home-only");
            Need(loadedReport.cookableNow.Contains(RecipeIndex("prepared_rice")),
                "Loaded, powered battery makes the equipped recipe cookable");

            foreach (int energy in new[] { 0, 1 })
            {
                var shortRun = Fixture(new[] { "E05" }, new[] { "B07" }, batteryLoaded: false, electricity: energy);
                var shortReport = MealPlan.Build(shortRun);
                var shortBattery = Line(shortReport, "electricity");
                Need(shortBattery != null && shortBattery.owned == energy && AtHome(shortBattery) == energy && shortBattery.missing == 2 - energy,
                    "Home battery level " + energy + " retains the real shortage for two batches");
                Need(shortBattery.note.Contains("电瓶在家") && shortBattery.note.Contains("装车") && shortBattery.estimatedCents == 0,
                    "Home battery level " + energy + " is disclosed without fabricating a replacement purchase");
                Need(!shortReport.cookableNow.Contains(RecipeIndex("prepared_rice")),
                    "Home battery level " + energy + " cannot power a loaded microwave");
            }
        }

        static RunState GasFixture(bool atHome, int gas)
        {
            var run = new RunState();
            run.HomeEquipment.Clear();
            run.LoadedEquipment.AddRange(new[] { "B04", "V01", "U01" });
            if (atHome) run.HomeEquipment.Add("E03"); else run.LoadedEquipment.Add("E03");
            run.Supplies["rice"] = 10;
            run.Supplies["P02"] = 10;
            var data = run.CaptureSave();
            data.stoveLoaded = true;
            data.gas = gas;
            run = RunState.Restore(data);
            Need(run.SetMealPlan("plain_rice", 2), "Set two-portion plain-rice plan");
            return run;
        }

        static void GasLocationAndShortage()
        {
            foreach (int gas in new[] { 0, 1, 7 })
            {
                var run = GasFixture(true, gas);
                var report = MealPlan.Build(run);
                var line = Line(report, "gas");
                Need(line != null && line.owned == gas && AtHome(line) == gas && line.missing == Math.Max(0, 2 - gas),
                    "Home gas " + gas + " reports the real level and shortage");
                Need(line.note.Contains("煤气罐在家") && line.note.Contains("装车") && line.estimatedCents == 0,
                    "Home gas " + gas + " is marked for loading without rebuying the cylinder");
                Need(!report.cookableNow.Contains(RecipeIndex("plain_rice")),
                    "Home gas " + gas + " is not usable by the loaded stove");
            }
            var loaded = MealPlan.Build(GasFixture(false, 7));
            var loadedGas = Line(loaded, "gas");
            Need(loadedGas != null && loadedGas.owned == 7 && AtHome(loadedGas) == 0 && !loadedGas.note.Contains("煤气罐在家"),
                "Loaded gas is distinguished from a cylinder at home");
            Need(loaded.cookableNow.Contains(RecipeIndex("plain_rice")),
                "Loaded gas powers the equipped stove");
        }

        static RunState MultipleBases(string recipe, string[] home, string[] loaded, int gas, int electricity, int cartridge = 0)
        {
            var run = new RunState();
            run.HomeEquipment.Clear();
            run.HomeEquipment.AddRange(home);
            run.LoadedEquipment.Clear();
            run.LoadedEquipment.AddRange(loaded);
            run.Supplies["rice"] = 10;
            run.Supplies["dough"] = 10;
            run.Supplies["P02"] = 10;
            run.Supplies["P04"] = 10;
            var data = run.CaptureSave();
            data.stoveLoaded = Array.IndexOf(loaded, "B04") >= 0;
            data.gas = gas + cartridge; // run's gas pool includes cartridge gas
            data.cartridge = cartridge;
            data.electricity = electricity;
            run = RunState.Restore(data);
            Need(run.SetMealPlan(recipe, 2), "Set multi-base plan " + recipe);
            return run;
        }

        static MealPlanLine UncertainEnergy(MealPlanReport report, string recipe)
        {
            var line = Line(report, "energy:" + recipe);
            Need(line != null && line.estimateUncertain && line.estimatedCents == 0,
                "Multiple bases keep energy demand and price uncertain for " + recipe);
            return line;
        }

        static void MultipleBasesOnlyRelevantHomeEnergy()
        {
            // Only E03 relates to these two viable bases. E05 and E01 are at
            // home but there is no electric/coal base in this setup.
            var run = MultipleBases("plain_rice", new[] { "E03", "E05", "E01" },
                new[] { "B03", "B02", "V01", "U01" }, 7, 45);
            string before = JsonUtility.ToJson(run.CaptureSave());
            var report = MealPlan.Build(run);
            var line = UncertainEnergy(report, "plain_rice");
            Need(line.note.Contains("煤气罐在家") && line.note.Contains("余 7 点") && line.note.Contains("装车"),
                "Multiple viable bases expose the real seven-point home gas conditionally");
            Need(!line.note.Contains("电瓶在家") && !line.note.Contains("蜂窝煤在家") && !line.note.Contains("卡式炉在家"),
                "Unrelated home devices and loaded cartridge stove are not suggested");
            Need(!report.cookableNow.Contains(RecipeIndex("plain_rice")),
                "Home gas does not power either loaded stove");
            Need(JsonUtility.ToJson(run.CaptureSave()) == before,
                "Multi-base plan never moves fuel, equipment, stock or cash");

            var loaded = MealPlan.Build(MultipleBases("plain_rice", new[] { "E05", "E01" },
                new[] { "B03", "B02", "V01", "U01", "E03" }, 7, 45));
            var loadedLine = UncertainEnergy(loaded, "plain_rice");
            Need(!loadedLine.note.Contains("煤气罐在家") && loaded.cookableNow.Contains(RecipeIndex("plain_rice")),
                "Already-loaded gas is usable and has no home-only notice");
        }

        static void MultipleBasesRespectVesselCompatibilityAndEmptyBattery()
        {
            // V01 can use B03, but not B05. A battery left at home must not be
            // suggested merely because an incompatible electric base exists.
            var incompatible = MealPlan.Build(MultipleBases("plain_rice", new[] { "E05" },
                new[] { "B03", "B05", "V01", "U01", "E03" }, 7, 45));
            var incompatibleLine = UncertainEnergy(incompatible, "plain_rice");
            Need(!incompatibleLine.note.Contains("电瓶在家"),
                "A home battery is not suggested for a base incompatible with the actual vessel");

            // V05 makes B05 a real electric alternative; E05 is at home and
            // empty. Another gas stove already works, so loading the battery
            // is conditional and does not force the electric route.
            var emptyBattery = MealPlan.Build(MultipleBases("plain_flatbread", new[] { "E05" },
                new[] { "B03", "B05", "V05", "U04", "E03" }, 7, 0));
            var emptyLine = UncertainEnergy(emptyBattery, "plain_flatbread");
            Need(emptyLine.note.Contains("电瓶在家") && emptyLine.note.Contains("余 0 点") && emptyLine.note.Contains("选用") && emptyLine.note.Contains("装车"),
                "Usable electric alternative discloses the home empty battery conditionally");
            Need(!emptyLine.note.Contains("煤气罐在家") && emptyBattery.cookableNow.Contains(RecipeIndex("plain_flatbread")),
                "Loaded gas remains a valid alternative; the empty home battery is not forced");
        }

        public static void Run()
        {
            checks.Clear();
            var report = new Report();
            try
            {
                HomeMicrowaveIsDisclosed();
                OneOfTwoHomeMicrowavesIsEnough();
                LoadedAlternativeDoesNotRequireHomeBackup();
                OrdinaryStoveCannotReplaceMicrowave();
                LoadedMicrowaveIsCookable();
                BatteryLocationAndShortage();
                GasLocationAndShortage();
                MultipleBasesOnlyRelevantHomeEnergy();
                MultipleBasesRespectVesselCompatibilityAndEmptyBattery();
                report.pass = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            report.checks = checks.ToArray();
            string path = Path.Combine(Application.dataPath, "..", "mealplan22-home-equipment-report.json");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--mealplan22-home-report") path = args[i + 1];
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.pass) throw new InvalidOperationException("MealPlan22 validation failed: " + report.error);
        }
    }
}
