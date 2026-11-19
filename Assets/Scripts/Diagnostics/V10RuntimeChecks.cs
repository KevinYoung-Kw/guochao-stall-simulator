using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Guochao
{
    public sealed class V10RuntimeChecks : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--interaction-test")>=0)
                new GameObject("V10InteractionDiagnostics").AddComponent<V10RuntimeChecks>();
        }
        private IEnumerator Start()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--interaction-output");
            string output=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"interaction-validation.json");
            float deadline=Time.realtimeSinceStartup+20;
            while((GameFlow.Instance==null||GameFlow.Instance.World==null)&&Time.realtimeSinceStartup<deadline)yield return null;
            GameFlow flow=GameFlow.Instance;Exception error=null;
            try
            {
                if(flow==null||flow.World==null)throw new InvalidOperationException("GameFlow did not initialize for the interaction diagnostic.");
                if(Array.IndexOf(args,"--capture-dir")>=0||Array.IndexOf(args,"--traversal-test")>=0)
                    throw new InvalidOperationException("Run interaction, capture and traversal diagnostics separately.");
                // Explicit fixture initialization, NOT a claimed Home traversal.
                // The actual movement test begins at the new Market's real Spawn.
                var fixture=new RunState();fixture.Act("HomeExit",true);
                typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,fixture);
                typeof(GameFlow).GetMethod("CreateWorld",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(flow,null);
                flow.SetPaused(false);
                Debug.Log("INTERACTION_DIAGNOSTIC_INITIALIZATION: fresh Market from Home business state; Home route and 15-second travel excluded. Movement begins at authored Spawn, no Teleport.");
            }
            catch(Exception exception){error=exception;}
            if(error!=null)
            {
                string text="INTERACTION_INITIALIZATION_FAIL\n"+error;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.WriteAllText(output,text);Debug.LogError(text);Application.Quit(1);yield break;
            }
            yield return null;
            bool passed=false;
            yield return ShoppingInteractionValidation.ValidateFreshMarket(flow,output,value=>passed=value);
            Application.Quit(passed?0:1);
        }

        // Call during a live opened BusinessSession to exercise the actual controller
        // pause gate. This is separate from the pure Session contract tests.
        public static IEnumerator ValidateBusinessPause(BusinessController business,Action<bool,string> completed)
        {
            GameFlow flow=GameFlow.Instance;
            if(flow==null||business?.Session==null||!business.Session.Started||business.Session.Finished)
            {completed?.Invoke(false,"Requires a live opened business session.");yield break;}
            flow.SetPaused(true);
            float elapsed=business.Session.Elapsed;int cash=business.Session.CashCents,revision=business.Session.Revision;
            string stock=InventoryStamp(business.Session);
            business.CloseDay();
            if(business.Session.Orders.Count>0)business.AcceptOrder(business.Session.Orders[0].id);
            if(business.Session.Lanes.Length>0){business.LaneAction(0);business.Pack(0,"P05");}
            yield return new WaitForSecondsRealtime(.5f);
            bool same=business.Session.Elapsed==elapsed&&business.Session.CashCents==cash&&business.Session.Revision==revision&&
                InventoryStamp(business.Session)==stock&&!business.Session.Finished;
            flow.SetPaused(false);yield return new WaitForSecondsRealtime(.15f);
            bool resumed=business.Session.Elapsed>elapsed;
            completed?.Invoke(same&&resumed,same&&resumed?"BUSINESS_CONTROLLER_PAUSE_PASS: clock, stock, commands and cash frozen; clock resumes.":
                "BUSINESS_CONTROLLER_PAUSE_FAIL same="+same+" resumed="+resumed);
        }
        private static string InventoryStamp(BusinessSession session)
        {
            var keys=new System.Collections.Generic.List<string>(session.Inventory.Keys);keys.Sort(StringComparer.Ordinal);
            var text=new System.Text.StringBuilder();foreach(string id in keys)text.Append(id).Append('=').Append(session.Inventory[id]).Append(';');return text.ToString();
        }
    }
}
