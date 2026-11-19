using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    public static class GameplayValidation
    {
        [MenuItem("Guochao/Validate playable slice")]
        public static void Run()
        {
            GameObject root = new GameObject("GameplayValidationRoot");
            try
            {
                GameFlow flow = root.AddComponent<GameFlow>();
                string report = GameplaySmokeChecks.RunAll(flow);
                Debug.Log(report);
                string output = Path.Combine(Application.dataPath, "../gameplay-validation.txt");
                File.WriteAllText(output, report);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("GAMEPLAY_SMOKE_FAIL\n" + exception);
                File.WriteAllText(Path.Combine(Application.dataPath, "../gameplay-validation.txt"), "GAMEPLAY_SMOKE_FAIL\n" + exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
            finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
