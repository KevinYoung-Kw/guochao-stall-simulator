using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Audited, untextured FBX materials only. Food, foliage, separate characters and AI
    // albedos keep their own colours; this is not a scene-wide tint or HSV filter.
    public static class AuthoredWorldPalette
    {
        private static readonly Color Ink = new Color32(35, 56, 77, 255);
        private static readonly Color Paper = new Color32(238, 228, 207, 255);
        private static readonly Color Steel = new Color32(164, 162, 156, 255);
        private static readonly Color Charcoal = new Color32(49, 50, 54, 255);
        private static readonly Color Vermilion = new Color32(185, 71, 50, 255);

        private static readonly Dictionary<string, Color> Home = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "teal", new Color32(59, 76, 93, 255) },
            { "teal_light", new Color32(215, 203, 180, 255) },
            { "tile", new Color32(216, 211, 200, 255) },
            { "metal", Steel },
            { "navy", Ink },
            { "glass", new Color32(177, 190, 204, 255) },
            { "outside", new Color32(184, 174, 158, 255) },
            { "white", Paper },
            { "Neutral metal", Steel },
            { "Ink rubber", Charcoal },
            { "Warm black bakelite", Charcoal },
            { "ENV_concrete", new Color32(180, 171, 156, 255) },
            { "ENV_mortar", new Color32(132, 124, 112, 255) },
            { "ENV_stone_dark", new Color32(152, 143, 129, 255) },
            { "ENV_stone_light", new Color32(204, 194, 176, 255) },
            { "ENV_roof", new Color32(101, 94, 89, 255) },
            { "ENV_teal", new Color32(75, 89, 105, 255) },
            { "ENV_dark", Charcoal },
            { "ENV_glass", new Color32(177, 190, 204, 255) },
            { "ENV_metal", new Color32(142, 138, 130, 255) },
            { "ENV_cloth", new Color32(216, 201, 173, 255) },
            { "ENV_red", Vermilion }
        };

        private static readonly Dictionary<string, Color> Market = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "V7_深绿烤漆", Ink },
            { "Shop deep green structure", new Color32(88, 100, 116, 255) },
            { "V7_冰柜浅绿", Paper },
            { "V7_暖灰砖", new Color32(185, 175, 158, 255) },
            { "建筑_浅灰", new Color32(220, 213, 199, 255) },
            { "网格_淡灰", new Color32(162, 153, 138, 255) },
            { "V7_屏幕", Ink },
            { "V7_拉丝钢", Steel },
            { "V7_深墨", Charcoal }
        };

        private static readonly Dictionary<string, Color> Checkout = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "Checkout_Jade", new Color32(65, 80, 96, 255) },
            { "Checkout_Mint", Paper },
            { "Checkout_Screen", Ink },
            { "Checkout_Lit", new Color32(232, 189, 100, 255) },
            { "Checkout_Rubber", Charcoal },
            { "Checkout_Steel", Steel },
            { "Checkout_Red", Vermilion }
        };

        // The independently imported shopping basket uses these same material
        // names, but its metal stays warm silver rather than the shop's paint.
        private static readonly Dictionary<string, Color> ShoppingCart = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            { "V7_拉丝钢", new Color32(174, 173, 168, 255) },
            { "V7_深绿烤漆", Ink },
            { "V7_深墨", Charcoal }
        };

        public static Color Resolve(GameObject model, Material source, Color original)
        {
            if (model == null || source == null) return original;
            // In particular, never multiply or replace the AI bed's blanket albedo.
            bool textured = (source.HasProperty("_MainTex") && source.GetTexture("_MainTex") != null)
                || (source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") != null);
            return TryResolve(model.name, source.name, textured, out Color colour) ? colour : original;
        }

        public static bool TryResolve(string modelName, string materialName, bool hasTexture, out Color colour)
        {
            colour = default;
            if (hasTexture || string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(materialName)) return false;
            Dictionary<string, Color> palette;
            switch (Scope(modelName))
            {
                case 1: palette = Home; break;
                case 2: palette = Market; break;
                case 3: palette = Checkout; break;
                case 4: palette = ShoppingCart; break;
                default: return false;
            }
            const string instanceSuffix = " (Instance)";
            if (materialName.EndsWith(instanceSuffix, StringComparison.Ordinal))
                materialName = materialName.Substring(0, materialName.Length - instanceSuffix.Length);
            return palette.TryGetValue(materialName, out colour);
        }

        // The renderer cache includes this scope, so a shared source Material
        // cannot transfer an environment recolour into an unrelated model.
        public static int Scope(string modelName)
        {
            switch (modelName)
            {
                case "HomeWorld":
                case "HomeWorld(Clone)": return 1;
                case "MarketWorld":
                case "MarketWorld(Clone)": return 2;
                case "MarketCheckoutDetails":
                case "MarketCheckoutDetails(Clone)":
                case "MarketCheckoutDetails_Imported": return 3;
                case "ShoppingCart":
                case "ShoppingCart(Clone)": return 4;
                default: return 0;
            }
        }
    }
}
