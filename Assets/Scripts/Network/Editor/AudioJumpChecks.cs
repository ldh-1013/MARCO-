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
        static readonly HashSet<AudioSource> jumps=new(), lands=new(), entries=new(), entryBaseline=new();
        static readonly HashSet<MeshFilter> jumpRings=new(), landRings=new();
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
                if(phase==3&&now>=until){jumps.Clear(); lands.Clear(); entries.Clear(); entryBaseline.Clear(); foreach(var a in round.GetComponentsInChildren<AudioSource>())if(a.clip!=null&&a.clip.name=="WaterEntry")entryBaseline.Add(a); jumpRings.Clear(); landRings.Clear(); vertical=Mathf.Sqrt(2*22*1.25f); previous=Time.time; until=now+1.4; phase=4; return;}
                if(phase==4)
                {
                    float dt=Mathf.Min(.03f,Time.time-previous); previous=Time.time;
                    var controller=player.GetComponent<CharacterController>();
                    if(controller.isGrounded&&vertical<0)vertical=-2; else vertical-=22*dt;
                    // Even trials stationary, odd trials horizontal movement.
                    controller.Move((Vector3.up*vertical+Vector3.forward*(trial%2==1?2f:0))*dt);
                    foreach(var source in round.GetComponentsInChildren<AudioSource>())
                    {if(source.volume>.01f&&source.name.StartsWith("Jump "))jumps.Add(source); if(source.volume>.01f&&source.name.StartsWith("Landing "))lands.Add(source); if(source.volume>.01f&&source.clip!=null&&source.clip.name=="WaterEntry"&&!entryBaseline.Contains(source))entries.Add(source);}
                    foreach(var mesh in UnityEngine.Object.FindObjectsByType<MeshFilter>())
                    {
                        if(mesh.name.StartsWith("Jump Floor Pulse "))jumpRings.Add(mesh);
                        if(mesh.name.StartsWith("Landing Floor Pulse "))landRings.Add(mesh);
                    }
                    if(now<until)return;
                    Debug.Log($"[AudioJumpChecks] role={player.Role}, moving={trial%2==1}, jumps={jumps.Count}, landings={lands.Count}, jumpRings={jumpRings.Count}, landRings={landRings.Count}, waterEntries={entries.Count}");
                                        if(trial<4)
                    {
                        // Take-off has a sound but no ring; landing has both (ring D3: Sprint-grade floor ring).
                        if(jumps.Count!=1||lands.Count!=1||jumpRings.Count!=0||landRings.Count!=1)
                            throw new Exception("Expected one dry takeoff sound without ring and one landing sound/ring per jump");
                    }
                    else
                    {
                        // Real water entry replaces dry landing; its ripple is covered by WaterReactionChecks.
                        if(jumps.Count!=1||jumpRings.Count!=0||entries.Count!=1||lands.Count!=0)
                            throw new Exception("Expected one wet takeoff (no ring) and one water entry without duplicate landing");
                    }
                    trial++;
                    if(trial==2||trial==5)typeof(NetworkRound).GetMethod("CheatToggleHostRole",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(round,null);
                    if(trial>=6){Debug.Log("[AudioJumpChecks] PASS: Seeker/Runner stationary and moving dry jumps; wet takeoff and water-entry replacement without duplicate landing."); Finish(0);return;}
                    until=now; phase=2;
                }
            }
            catch(Exception ex){Debug.LogException(ex);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Pending,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
