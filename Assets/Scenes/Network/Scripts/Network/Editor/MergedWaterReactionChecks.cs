using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Marco.Network.Editor {
 [InitializeOnLoad] public static class MergedWaterReactionChecks {
 const string Pending="Marco.MergedWaterReactionChecks"; static double deadline;
 static MergedWaterReactionChecks(){if(SessionState.GetBool(Pending,false))Subscribe();}
 static void Subscribe(){deadline=EditorApplication.timeSinceStartup+110;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
 public static void Run(){
 if(!Application.isBatchMode)throw new Exception("Batch-only integration check");
 EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity",OpenSceneMode.Single);
 SessionState.SetBool(Pending,true);Subscribe();WaterReactionChecks.Run();
 }
 static void Tick(){
 if(EditorApplication.timeSinceStartup>deadline){Finish(false,"Timeout waiting for incoming water reaction checks");return;}
 if(SessionState.GetBool("Marco.WaterReactionChecks",false)||EditorApplication.isPlayingOrWillChangePlaymode)return;
 string path="Logs/PoolWater/player-result.txt";
 string result=File.Exists(path)?File.ReadAllText(path):"Missing water reaction result";
 Finish(result.StartsWith("PASS"),result);
 }
 static void Finish(bool passed,string result){SessionState.SetBool(Pending,false);EditorApplication.update-=Tick;if(passed)Debug.Log("[MergedWater] "+result);else Debug.LogError("[MergedWater] "+result);EditorApplication.Exit(passed?0:1);}
 }
}