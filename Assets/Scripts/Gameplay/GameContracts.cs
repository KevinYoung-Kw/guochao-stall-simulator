using System;
using UnityEngine;

namespace Guochao
{
    public enum GamePhase { Home, Market, Complete, Error, SiteSelection, Business, DaySummary, Night }

    // A read-only-by-convention UI snapshot, replaced after each state change.
    [Serializable]
    public sealed class Snapshot
    {
        public GamePhase phase;
        public float remaining;
        public float cash;
        public int cartCount;
        public int cartCellsUsed;
        public float cartTotal;
        public bool paid;
        public bool loaded;
        public string heldItem = "";
        public string contextPrompt = "";
        public string actionBlockedReason = "";
        public bool canInteract;
        public bool primaryEnabled;
        public bool checkoutInProgress;
        public float checkoutRemaining;
        public float checkoutDuration;
        public string startTime = "08:00";
        public string[] itemIds = Array.Empty<string>();
        public string message = "";
        public bool pause;
        public bool stoveLoaded;
        public int loadedCount;
        public float amountSpent;
        public float marketUsedSeconds;
        public string targetId = "";
        public Vector3 contextWorldPosition;
        public string objective = "";
        public bool timeoutSettlement;
        public bool busy;
        public bool transitioning;
        public bool pushingCart;
        public bool canToggleCart;
        public bool isRunning;
        public string cartPrompt = "";
        public int cartCapacity = 9;
        public string careerName = "";
        public int dayIndex=1;
    }

    public sealed class ItemDefinition
    {
        public readonly string id;
        public readonly string name;
        public readonly int portions;
        public readonly int priceCents;
        public readonly string anchor;
        public readonly Color color;
        public float Price => priceCents / 100f;

        public ItemDefinition(string id, string name, int portions, int priceCents, string anchor, Color color)
        { this.id = id; this.name = name; this.portions = portions; this.priceCents = priceCents; this.anchor = anchor; this.color = color; }
    }

    public static class GameCatalog
    {
        public const string StoveId = "B04";
        public static readonly ItemDefinition[] Items =
        {
            new ItemDefinition("rice", "大米河粉面条袋", 15, 1350, "RiceShelf", new Color(.88f,.71f,.39f)),
            new ItemDefinition("meat", "精选鲜肉肥肠海鲜筐", 10, 3150, "MeatShelf", new Color(.80f,.30f,.25f)),
            new ItemDefinition("egg", "家常鸡蛋青菜筐", 12, 1620, "EggShelf", new Color(.48f,.65f,.30f)),
            new ItemDefinition("dough", "煎烙饼胚冷面臭豆腐胚箱", 15, 1620, "DoughShelf", new Color(.82f,.64f,.38f)),
            new ItemDefinition("skewer", "串串淀粉肠年糕半成品袋", 15, 2430, "SkewerShelf", new Color(.69f,.35f,.19f)),
            new ItemDefinition("prepared", "速冻盖饭预制料理包箱", 30, 8100, "PreparedShelf", new Color(.34f,.53f,.64f)),
            new ItemDefinition("fruit", "鲜柠原叶鲜奶冰粉料组", 20, 3960, "FruitShelf", new Color(.62f,.71f,.26f)),
            new ItemDefinition("seasoning", "通用油盐秘制酱料桶", 50, 2250, "SeasoningShelf", new Color(.59f,.32f,.13f))
        };
        public static ItemDefinition Find(string id) => Array.Find(Items, item => item.id == id) ?? SupplyCatalog.Find(id);
    }
}
