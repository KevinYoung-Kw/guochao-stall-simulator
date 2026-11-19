using System;
using System.IO;
using UnityEngine;
namespace Guochao.Editor {
 public static class Freedom19AggregateValidation {
  public static void Run() {
   Freedom19CoreValidation.Run();
   string smoke=GameplaySmokeChecks.RunModelChecks();Debug.Log(smoke);
   var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--freedom19-core-report");
   if(i>=0)File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[i+1]),"smoke.txt"),smoke);
   Freedom19ChainValidation.Run();
  }
 }
}
