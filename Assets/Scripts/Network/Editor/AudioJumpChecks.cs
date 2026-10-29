using System;
using System.Collections.Generic;
using System.Reflection;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Marco.Network.Editor
{
    [InitializeOnLoad]
    public static class AudioJumpChecks
    {
        const string Pending="Marco.AudioJumpChecks";
        static int phase, trial; static double deadline, until;
        static float previous, vertical;
        static readonly HashSet<AudioSource> jumps=new(), lands=new();
        static AudioJumpChecks(){if(SessionState.GetBool(Pending,false)) Subscribe();}
        static void Subscribe(){deadline=EditorApplication.timeSinceStartup+90; EditorApplication.update-=Tick; EditorApplication.update+=Tick;}
        public static void Run(){if(!Application.isBatchMode)throw new Exception("Batch only"); EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity",OpenSceneMode.Single); SessionState.SetBool(Pending,true); Subscribe(); EditorApplication.EnterPlaymode();}
        static void Tick()
        {
            try
            {
                double now=EditorApplication.timeSinceStartup;
                if(now>deadline)throw new Exception("Jump test timeout phase "+phase);
                if(!EditorApplication.isPlaying)return;
                var round=NetworkRound.Instance; var player=NetworkPlayer.Local;
                if(phase==0){var lan=UnityEngine.Object.FindAnyObjectByType<LanConnector>(); if(lan==null)return; lan.Host(); phase++; return;}
                if(phase==1){if(player==null||round==null||!round.IsClientInitialized)return; round.StartRound(); until=now+1; phase++; return;}
                if(phase==2&&now>=until)
                {
                    player.GetComponent<PlayerMovement>().enabled=false;
                    player.GetComponent<PlayerInteractor>().enabled=false;
                    player.GetComponent<PlayerMovement>().Teleport(trial<2?new Vector3(1,1.05f,40):trial<4?new Vector3(56,1.05f,33):new Vector3(4.5f,-.15f,43));
                    Physics.SyncTransforms(); until=now+.3; phase=3; return;
                }
                if(phase==3&&now>=until){jumps.Clear(); lands.Clear(); vertical=Mathf.Sqrt(2*22*1.25f); previous=Time.time; until=now+1.4; phase=4; return;}
                if(phase==4)
                {
                    float dt=Mathf.Min(.03f,Time.time-previous); previous=Time.time;
                    var controller=player.GetComponent<CharacterController>();
                    if(controller.isGrounded&&vertical<0)vertical=-2; else vertical-=22*dt;
                    // Even trials stationary, odd trials horizontal movement.
                    controller.Move((Vector3.up*vertical+Vector3.forward*(trial%2==1?2f:0))*dt);
                    foreach(var source in round.GetComponentsInChildren<AudioSource>())
                    {if(source.volume>.01f&&source.name.StartsWith("Jump "))jumps.Add(source); if(source.volume>.01f&&source.name.StartsWith("Landing "))lands.Add(source);}
                    if(now<until)return;
                    Debug.Log($"[AudioJumpChecks] role={player.Role}, moving={trial%2==1}, jumps={jumps.Count}, landings={lands.Count}");
                    if(jumps.Count!=1||lands.Count!=1)throw new Exception("Expected one takeoff and one landing per jump");
                    trial++;
                    if(trial==2||trial==5)typeof(NetworkRound).GetMethod("CheatToggleHostRole",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(round,null);
                    if(trial>=6){Debug.Log("[AudioJumpChecks] PASS: Seeker/Runner stationary and moving jumps, including shallow-water contacts with nonzero sound volume."); Finish(0);return;}
                    until=now; phase=2;
                }
            }
            catch(Exception ex){Debug.LogException(ex);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Pending,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
