using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Guochao
{
    // One editable copy bank for the stall HUD, checkout, and closing receipt.
    // Translated wording and formatting never drive gameplay decisions.
    internal static class StallUiCopy
    {
        [Serializable] private sealed class Line { public string id = "", text = ""; }
        [Serializable] private sealed class Bank { public Line[] lines = Array.Empty<Line>(); }
        private static Dictionary<string, string> copy;

        internal static string Text(string id, string fallback)
        {
            if (copy == null)
            {
                copy = new Dictionary<string, string>(StringComparer.Ordinal);
                TextAsset asset = Resources.Load<TextAsset>("UI/StallUi.zh-CN");
                if (asset != null)
                {
                    try
                    {
                        foreach (Line line in JsonUtility.FromJson<Bank>(asset.text)?.lines ?? Array.Empty<Line>())
                        {
                            if (line == null || string.IsNullOrWhiteSpace(line.id) || string.IsNullOrWhiteSpace(line.text))
                                continue;
                            copy[line.id] = line.text;
                        }
                    }
                    catch (ArgumentException)
                    {
                        Debug.LogWarning("Could not load the stall UI copy bank.");
                    }
                }
            }
            return copy.TryGetValue(id, out string text) ? text : fallback;
        }

        internal static string Format(string id, string fallback, params object[] args)
        {
            try { return string.Format(CultureInfo.InvariantCulture, Text(id, fallback), args); }
            catch (FormatException) { return string.Format(CultureInfo.InvariantCulture, fallback, args); }
        }
    }
}
