using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Guochao
{
    public sealed class PlayableCapture : MonoBehaviour
    {
        // Read-only probe for the browser input regression. No game state is changed.
        [UnityEngine.Scripting.Preserve]
        public void InspectRuntime()
        {
            GameFlow flow = GameFlow.Instance;
            if (flow == null || flow.World == null) return;
            Debug.Log("QA_RUNTIME " + JsonUtility.ToJson(new RuntimeSample {
                snapshot = flow.State, position = flow.World.Player.transform.position,
                heading = flow.World.Player.visual.eulerAngles.y,
                stovePosition = flow.World.StoveVisualPosition,
                customers = flow.World.Crowd ? flow.World.Crowd.Positions : Array.Empty<Vector3>(),
                walkingCustomers = flow.World.Crowd ? flow.World.Crowd.WalkingCount : 0,
                customerTravelMetres = flow.World.Crowd ? flow.World.Crowd.TravelledMetres : 0
            }));
        }
        [UnityEngine.Scripting.Preserve]
        public void InspectBusiness()
        {
            var controller=GameFlow.Instance?.Business;
            if(controller!=null)Debug.Log("QA_BUSINESS "+JsonUtility.ToJson(controller.CaptureAudit()));
        }
        [Serializable] private class RuntimeSample
        {
            public Snapshot snapshot;
            public Vector3 position;
            public float heading;
            public Vector3 stovePosition;
            public Vector3[] customers;
            public int walkingCustomers;
            public float customerTravelMetres;
        }

        [UnityEngine.Scripting.Preserve]
        public void InspectKeyboardRoute(string target)
        {
            try { Debug.Log("QA_ROUTE " + TraversalValidation.ReadOnlyKeyboardHint(GameFlow.Instance, target)); }
            catch (Exception error) { Debug.Log("QA_ROUTE_ERROR " + error.Message); }
        }

        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--capture-dir");
            if (index < 0 || index + 1 >= args.Length) yield break;
            string folder = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(folder);
            yield return new WaitForSeconds(3);
            GameFlow flow = GameFlow.Instance;
            if (flow == null || flow.State.phase == GamePhase.Error)
            { File.WriteAllText(Path.Combine(folder, "capture-error.txt"), flow == null ? "Missing GameFlow" : flow.State.message); Application.Quit(1); yield break; }
            yield return Shot(folder, "01-出租屋实际运行.png");
            At(flow, "StovePickup"); yield return new WaitForSeconds(.3f); flow.PrimaryAction();
            yield return new WaitForSeconds(.5f); yield return Shot(folder, "02-拿取真实灶台.png");
            At(flow, "HomeCart"); yield return new WaitForSeconds(.2f); flow.PrimaryAction();
            At(flow, "HomeExit"); yield return new WaitForSeconds(.2f); flow.PrimaryAction();
            yield return new WaitForSeconds(.8f); yield return Shot(folder, "03-超市实际运行.png");
            foreach (string shelf in new[] { "RiceShelf", "MeatShelf", "EggShelf" })
            { At(flow, shelf); yield return new WaitForSeconds(.25f); flow.PrimaryAction(); }
            At(flow, "Cashier"); yield return new WaitForSeconds(.25f); flow.PrimaryAction();
            yield return new WaitForSeconds(1); yield return Shot(folder, "04-人工收银服务中.png");
            yield return new WaitForSeconds(4); yield return Shot(folder, "05-已付款待装车.png");
            At(flow, "MarketCart"); yield return new WaitForSeconds(.25f); flow.PrimaryAction();
            yield return new WaitForSeconds(.5f); yield return Shot(folder, "06-实际装车状态.png");
            At(flow, "MarketExit"); yield return new WaitForSeconds(.25f); flow.PrimaryAction();
            yield return new WaitForSeconds(.5f); yield return Shot(folder, "07-整备结果.png");
            File.WriteAllText(Path.Combine(folder, "capture-result.json"), JsonUtility.ToJson(flow.State, true));
            Application.Quit(0);
        }
        static void At(GameFlow flow, string anchor)
        {
            if (flow.World != null && flow.World.HasAnchor(anchor))
            {
                Vector3 position = flow.World.Anchor(anchor);
                if (flow.World.ShoppingMotor != null)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        Quaternion facing = Quaternion.Euler(0, i * 22.5f, 0);
                        if (!flow.World.ShoppingMotor.IsPoseClear(position, facing)) continue;
                        flow.World.ShoppingMotor.ResetPoseForValidation(position, facing);
                        return;
                    }
                    throw new InvalidOperationException("Capture anchor has no clear shopper/cart pose: " + anchor);
                }
                flow.World.Player.Teleport(position); Physics.SyncTransforms();
            }
        }
        static IEnumerator Shot(string folder, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name));
            yield return new WaitForSeconds(.35f);
        }
    }
}
