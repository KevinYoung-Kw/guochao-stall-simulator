namespace Guochao
{
    public sealed class VehicleUpgradeSpec
    {
        public readonly int Level;
        public readonly int Columns;
        public readonly int Rows;
        public readonly int PriceCents;
        public readonly float WeightLimitKg;
        public readonly string Name;

        public VehicleUpgradeSpec(
            int level,
            int columns,
            int rows,
            int priceCents,
            float weightLimitKg,
            string name)
        {
            Level = level;
            Columns = columns;
            Rows = rows;
            PriceCents = priceCents;
            WeightLimitKg = weightLimitKg;
            Name = name;
        }
    }

    public static class VehicleUpgradeRules
    {
        private static readonly VehicleUpgradeSpec Level1 = new VehicleUpgradeSpec(
            level: 1,
            columns: 4,
            rows: 6,
            priceCents: 0,
            weightLimitKg: 100f,
            name: "破旧脚蹬三轮");

        private static readonly VehicleUpgradeSpec Level2 = new VehicleUpgradeSpec(
            level: 2,
            columns: 5,
            rows: 6,
            priceCents: 100000,
            weightLimitKg: 180f,
            name: "电瓶改装三轮");

        public static VehicleUpgradeSpec Get(int level)
        {
            if (level == 0 || level == 1)
            {
                return Level1;
            }

            if (level == 2)
            {
                return Level2;
            }

            return null;
        }

        public static bool TryGet(int level, out VehicleUpgradeSpec spec, out string feedback)
        {
            spec = Get(level);
            if (spec != null)
            {
                feedback = "";
                return true;
            }

            feedback = "该载具等级尚未接入，现有等级已保留";
            return false;
        }
    }
}
