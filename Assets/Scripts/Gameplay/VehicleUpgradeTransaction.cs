using System;

namespace Guochao
{
    public static class VehicleUpgradeTransaction
    {
        private const int UpgradePriceCents = 100000;
        private const int UpgradedVehicleLevel = 2;
        private const string ReadyFeedback = "升级电瓶三轮 · ¥1000";
        private const string SaveFailedFeedback = "保存失败，升级未生效";
        private const string UpgradedFeedback = "电瓶三轮改装完成 · 花费 ¥1000";

        public static bool TryQuote(CampaignState c, RunState r, out int priceCents, out string feedback)
        {
            priceCents = 0;

            if (c == null || r == null)
            {
                feedback = "存档或运行状态缺失，无法升级";
                return false;
            }

            if (r.Phase != GamePhase.Night)
            {
                feedback = "仅可在夜间结算阶段升级车辆";
                return false;
            }

            if (c.vehicleLevel != 0 && c.vehicleLevel != 1)
            {
                feedback = c.vehicleLevel > 1
                    ? "当前已升级电瓶三轮，无需重复升级"
                    : "当前车辆等级异常，无法升级";
                return false;
            }

            var lastDay = c.lastDay;
            if (lastDay == null)
            {
                feedback = "缺少当日结算记录，无法升级";
                return false;
            }

            if (!lastDay.committed)
            {
                feedback = "当日结算尚未提交，无法升级";
                return false;
            }

            if (lastDay.dayIndex != c.dayIndex)
            {
                feedback = "当日结算记录与当前天数不一致，无法升级";
                return false;
            }

            if (c.history == null)
            {
                feedback = "缺少历史结算记录，无法升级";
                return false;
            }

            var twin = lastDay;
            bool hasTwin = false;
            foreach (var entry in c.history)
            {
                if (entry != null && entry.committed && entry.dayIndex == c.dayIndex && entry.sessionId == lastDay.sessionId)
                {
                    twin = entry;
                    hasTwin = true;
                    break;
                }
            }

            if (!hasTwin)
            {
                feedback = "历史记录中未找到当日已提交的结算条目，无法升级";
                return false;
            }

            if (r.CashCents != c.cashCents
                || c.cashCents != lastDay.closingCashCents
                || lastDay.closingCashCents != twin.closingCashCents)
            {
                feedback = "现金与结算记录不一致，无法升级";
                return false;
            }

            if (lastDay.vehicleUpgradeCents != twin.vehicleUpgradeCents)
            {
                feedback = "当日与历史记录的车辆升级支出不一致，无法升级";
                return false;
            }

            if (lastDay.vehicleUpgradeCents < 0 || lastDay.vehicleUpgradeCents > int.MaxValue - UpgradePriceCents)
            {
                feedback = "车辆升级支出记录超出有效范围，无法升级";
                return false;
            }

            if (r.CashCents < UpgradePriceCents)
            {
                feedback = "现金不足，升级电瓶三轮需要 ¥1000";
                return false;
            }

            priceCents = UpgradePriceCents;
            feedback = ReadyFeedback;
            return true;
        }

        public static bool TryUpgrade(CampaignState c, RunState r, Func<bool> save, out string feedback)
        {
            int priceCents;
            if (!TryQuote(c, r, out priceCents, out feedback))
            {
                return false;
            }

            var lastDay = c.lastDay;
            var twin = lastDay;
            bool hasTwin = false;
            foreach (var entry in c.history)
            {
                if (entry != null && entry.committed && entry.dayIndex == c.dayIndex && entry.sessionId == lastDay.sessionId)
                {
                    twin = entry;
                    hasTwin = true;
                    break;
                }
            }

            if (!hasTwin)
            {
                feedback = "历史记录中未找到当日已提交的结算条目，无法升级";
                return false;
            }

            bool sameLedger = object.ReferenceEquals(twin, lastDay);

            var oldVehicleLevel = c.vehicleLevel;
            var oldCampaignCashCents = c.cashCents;
            var oldRunCashCents = r.CashCents;
            var oldLastDayClosingCashCents = lastDay.closingCashCents;
            var oldLastDayVehicleUpgradeCents = lastDay.vehicleUpgradeCents;
            var oldTwinClosingCashCents = twin.closingCashCents;
            var oldTwinVehicleUpgradeCents = twin.vehicleUpgradeCents;

            int newCashCents = oldCampaignCashCents - priceCents;

            c.vehicleLevel = UpgradedVehicleLevel;
            c.cashCents = newCashCents;
            r.SetBusinessCash(oldRunCashCents - priceCents);
            lastDay.closingCashCents = newCashCents;
            twin.closingCashCents = newCashCents;
            lastDay.vehicleUpgradeCents = oldLastDayVehicleUpgradeCents + priceCents;
            if (!sameLedger)
            {
                twin.vehicleUpgradeCents = oldTwinVehicleUpgradeCents + priceCents;
            }

            bool saved = false;
            if (save != null)
            {
                try
                {
                    saved = save();
                }
                catch
                {
                    saved = false;
                }
            }

            if (!saved)
            {
                c.vehicleLevel = oldVehicleLevel;
                c.cashCents = oldCampaignCashCents;
                r.SetBusinessCash(oldRunCashCents);
                lastDay.closingCashCents = oldLastDayClosingCashCents;
                lastDay.vehicleUpgradeCents = oldLastDayVehicleUpgradeCents;
                if (!sameLedger)
                {
                    twin.closingCashCents = oldTwinClosingCashCents;
                    twin.vehicleUpgradeCents = oldTwinVehicleUpgradeCents;
                }

                feedback = SaveFailedFeedback;
                return false;
            }

            feedback = UpgradedFeedback;
            return true;
        }
    }
}
