using System;
using System.Reflection;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    [InitializeOnLoad]
    public static class MovementPulsePlayChecks
    {
        const string Pending="Marco.MovementPulsePlayChecks";
        static int phase,trial,count;
        static double deadline,until;
        static FootProbeDriver driver;
        static bool seen;
        static bool heardEarlyDryStep;
        static MovementPulsePlayChecks(){if(SessionState.GetBool(Pending,false))Subscribe();}
        static void Subscribe(){deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        public static void Run()
        {
            if(!Application.isBatchMode)throw new Exception("Batch only");
            EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity",OpenSceneMode.Single);
            SessionState.SetBool(Pending,true);Subscribe();EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                double now=EditorApplication.timeSinceStartup;
                if(now>deadline)throw new Exception("Play check timed out phase "+phase);
                if(!EditorApplication.isPlaying)return;
                var round=NetworkRound.Instance;var player=NetworkPlayer.Local;
                if(phase==0){var lan=UnityEngine.Object.FindAnyObjectByType<LanConnector>();if(lan==null)return;lan.Host();phase=1;return;}
                if(phase==1){if(player==null||round==null||!round.IsClientInitialized)return;round.StartRound();until=now+1;phase=2;return;}
                if(phase==2&&now>=until)
                {
                    player.GetComponent<PlayerMovement>().enabled=false;
                    player.GetComponent<PlayerInteractor>().enabled=false;
                    player.GetComponent<PlayerMovement>().Teleport(new Vector3(1,1.05f,40));
                    Physics.SyncTransforms();until=now+.4;phase=3;return;
                }
                if(phase==3&&now>=until)
                {
                    driver = player.GetComponent<FootProbeDriver>() ?? player.gameObject.AddComponent<FootProbeDriver>();
                    driver.ResetTravel();
                    driver.Speed = trial % 2 == 0 ? 3.5f : 7.5f;
                    driver.Axis = Vector3.forward;
                    driver.Gravity = 2f;
                    driver.Drive = true;
                    seen=false;heardEarlyDryStep=false;count=0;phase=4;return;
                }
                if(phase==4)
                {

                    if (driver.Distance < 5f)
                        foreach (var source in round.GetComponentsInChildren<AudioSource>())
                            if (source.name.StartsWith("SFX Tile_") || source.name.StartsWith("SFX Concrete_") ||
                                source.name.StartsWith("SFX Carpet") || source.name.StartsWith("SFX Mat") ||
                                source.name.StartsWith("SFX Metal")) heardEarlyDryStep = true;
                    foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
                    {
                        if(filter.name!="Movement Floor Pulse "+player.PlayerId)continue;
                        var visualState=player.GetComponentInChildren<Marco.Network.Vision.WalkingFootRings>();
                        var active=(System.Collections.IList)typeof(Marco.Network.Vision.WalkingFootRings).GetField("rings",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(visualState);
                        foreach(var entry in active)
                        {
                            var type=entry.GetType();
                            if((int)type.GetField("Source").GetValue(entry)!=player.PlayerId)continue;
                            float expected=trial%2==1?5f:1.5f;
                            if((bool)type.GetField("Expand").GetValue(entry) ||
                                !Mathf.Approximately((float)type.GetField("Radius").GetValue(entry),expected))
                                throw new Exception("Movement ring expanded or used the wrong radius");
                        }
                        seen=true;count++;
                    }
                    if(driver.Distance<7.35f)return;
                    driver.Drive = false;
                    until=now+.05;phase=5;return;
                }
                if(phase==5&&now>=until)
                {
                    if (driver.Distance < 5f)
                        foreach (var source in round.GetComponentsInChildren<AudioSource>())
                            if (source.name.StartsWith("SFX Tile_") || source.name.StartsWith("SFX Concrete_") ||
                                source.name.StartsWith("SFX Carpet") || source.name.StartsWith("SFX Mat") ||
                                source.name.StartsWith("SFX Metal")) heardEarlyDryStep = true;
                    foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())if(filter.name=="Movement Floor Pulse "+player.PlayerId)seen=true;
                    if(!seen)throw new Exception("No movement ring for "+player.Role+" trial "+trial);
                    if (!heardEarlyDryStep) throw new Exception("No local dry footstep before 5m for " + player.Role);
                    Debug.Log($"[MovementPulsePlayChecks] role={player.Role} speed={driver.Speed} ring observed");
                    var visual=player.GetComponentInChildren<Marco.Network.Vision.WalkingFootRings>();
                    var point=player.transform.position;point.y=player.GetComponent<CharacterController>().bounds.min.y+.025f;
                    visual.Show(player.PlayerId,point,false,player.Role==Marco.Core.WinCondition.PlayerRole.Seeker);
                    var list=(System.Collections.IList)typeof(Marco.Network.Vision.WalkingFootRings).GetField("rings",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(visual);
                    var first=list[list.Count-1];var firstMesh=first.GetType().GetField("Mesh").GetValue(first);
                    visual.Show(player.PlayerId,point,false,player.Role==Marco.Core.WinCondition.PlayerRole.Seeker);
                    var second=list[list.Count-1];
                    if(!ReferenceEquals(first,second)||!ReferenceEquals(firstMesh,second.GetType().GetField("Mesh").GetValue(second)))throw new Exception("Repeated pulse did not reuse its object/mesh");
                    var ringType=second.GetType();
                    if((bool)ringType.GetField("Expand").GetValue(second))throw new Exception("Walking ring must not expand");
                    var walkMesh=(Mesh)ringType.GetField("Mesh").GetValue(second);
                    float extent=0;foreach(var v in walkMesh.vertices)extent=Mathf.Max(extent,new Vector2(v.x,v.z).magnitude);
                    if(extent<.5f)throw new Exception("Walking ring was spawned at the tiny expanding radius");
                    int total=list.Count;
                    visual.Show(99999,point,false,false);
                    if(list.Count!=total)throw new Exception("Remote walking ring must stay invisible");
                    visual.Show(99999,point,true,false);
                    var remote=list[list.Count-1];
                    if((int)remote.GetType().GetField("Source").GetValue(remote)!=99999 || (bool)remote.GetType().GetField("Expand").GetValue(remote))throw new Exception("Remote sprint ring missing or expanding");
                    visual.ShowSound(point + Vector3.up, 9f, 1.2f);
                    var sound = list[list.Count - 1];
                    if (!(bool)sound.GetType().GetField("Expand").GetValue(sound) ||
                        !Mathf.Approximately((float)sound.GetType().GetField("Lifetime").GetValue(sound), 1.2f))
                        throw new Exception("Sound pulse did not use its supplied lifetime and expansion");
                    var policy=typeof(NetworkRound).GetMethod("CanReceiveSound",BindingFlags.Static|BindingFlags.NonPublic);
                    bool Allowed(string clip,int source,int listener)=>(bool)policy.Invoke(null,new object[]{clip,source,listener});
                    if(!Allowed("Breath",7,7)||Allowed("Breath",7,8)||Allowed("Breath",-1,8)||Allowed("Breath_Runner",7,8)||!Allowed("Valve",7,8))throw new Exception("Breath self-only audience policy failed");
                    trial++;
                    if(trial==2)typeof(NetworkRound).GetMethod("CheatToggleHostRole",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(round,null);
                    if(trial==4){Debug.Log("[MovementPulsePlayChecks] PASS: both roles walk/sprint; owner-only static walk; remote fixed sprint; expanding sound rings; pooled reuse; self-only breath policy. Multi-PC listening and GPU appearance remain manual.");Finish(0);return;}
                    until=now+.6;phase=2;
                }
            }
            catch(Exception ex){Debug.LogException(ex);Finish(1);}
        }
        static void Finish(int code){SessionState.SetBool(Pending,false);EditorApplication.update-=Tick;EditorApplication.Exit(code);}
    }
}
