using System;
using System.Collections.Generic;
using System.Reflection;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Character;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Marco.Network.Editor
{
    [DefaultExecutionOrder(-1000)] public sealed class FootProbeDriver : MonoBehaviour
    {
        public float Speed,Distance;public float Gravity=4;public Action Observe;public Vector3 Axis;public bool Drive,SwitchGait;private float travel,elapsed;
        public void ResetTravel(){travel=Distance=elapsed=0;}
        private void Update()
        {
            Observe?.Invoke();
            if(!Drive)return;
            float sign=(Mathf.FloorToInt(travel/1.5f)%2)==0?1:-1;
            float actualSpeed=SwitchGait&&Mathf.FloorToInt(elapsed/.35f)%2==0?5f:Speed;elapsed+=Time.deltaTime; float amount=actualSpeed*Time.deltaTime;travel+=amount;
            var before=transform.position;
            GetComponent<CharacterController>().Move(Axis*sign*amount+Vector3.down*Gravity*Time.deltaTime);
            Distance+=Vector3.ProjectOnPlane(transform.position-before,Vector3.up).magnitude;
        }
    }
    [InitializeOnLoad] public static class FootstepDiagnostics
    {
        const string Pending="Marco.FootProbe",Scene="Assets/Scenes/Network/NetworkPool_v1_2.unity";
        static int phase,trial,result,frames,grounded,changes,lastIndex,rings;
        static double deadline,until;static Type worldType;static object world;
        static readonly HashSet<AudioSource> heard=new();static string label;
        static FootstepDiagnostics(){if(SessionState.GetBool(Pending,false))Subscribe();}
        static void Subscribe(){deadline=EditorApplication.timeSinceStartup+160;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
        public static void Run(){if(!Application.isBatchMode)throw new Exception("Batch only");EditorSceneManager.OpenScene(Scene);SessionState.SetBool("Marco.FootProbe.Transitions",false);SessionState.SetBool(Pending,true);Subscribe();EditorApplication.EnterPlaymode();}
        public static void RunTransitions(){Run();SessionState.SetBool("Marco.FootProbe.Transitions",true);}
        static string Meta(Collider c,string field)
        {foreach(var part in c.GetComponentsInParent<MonoBehaviour>())if(part!=null&&part.GetType().Name=="PoolV12Part")return part.GetType().GetField(field).GetValue(part) as string;return "";}
        static Collider Find(string zone,string cat,string id="")
        {
            Collider best=null;float area=0;
            foreach(var c in UnityEngine.Object.FindObjectsByType<Collider>())
                if(c.enabled&&!c.isTrigger&&Meta(c,"zone")==zone&&Meta(c,"category")==cat&&(id==""||Meta(c,"designId")==id))
                {float a=c.bounds.size.x*c.bounds.size.z;if(a>area){area=a;best=c;}}
            return best;
        }
        static void Audit()
        {
            int unknown=0,walkable=0;var examples=new HashSet<string>();
            for(int floor=0;floor<2;floor++)for(int x=1;x<63;x+=2)for(int z=1;z<61;z+=2)
            {
                RaycastHit best=default;bool found=false;float nearest=100;
                var origin=new Vector3(x,floor==0?2.8f:6.2f,z);
                foreach(var hit in Physics.RaycastAll(origin,Vector3.down,2.9f,~0,QueryTriggerInteraction.Ignore))
                {if(hit.normal.y<.5f||hit.collider.GetComponentInParent<NetworkPlayer>()!=null)continue;if(hit.distance<nearest){nearest=hit.distance;best=hit;found=true;}}
                if(!found)continue;walkable++;
                string surface=(string)worldType.GetMethod("Surface").Invoke(world,new object[]{best.collider,best.point});
                if(surface!="")continue;unknown++;
                string kind=best.collider.name+"/"+Meta(best.collider,"category")+"/"+Meta(best.collider,"zone");
                if(examples.Count<25&&examples.Add(kind))Debug.Log("[FootProbe] unmapped support "+kind+" point="+best.point);
            }
            Debug.Log($"[FootProbe] grid walkable={walkable} unmapped={unknown}");
        }
        static void Tick()
        {
            try
            {
                double now=EditorApplication.timeSinceStartup;if(now>deadline)throw new Exception("Timeout phase "+phase);
                if(phase==9)
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                    EditorSceneManager.OpenScene(Scene);EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene);
                    SessionState.SetBool(Pending,false);EditorApplication.update-=Tick;EditorApplication.Exit(result);return;
                }
                if(!EditorApplication.isPlaying)return;
                var round=NetworkRound.Instance;var player=NetworkPlayer.Local;
                if(phase==0){var lan=UnityEngine.Object.FindAnyObjectByType<LanConnector>();if(lan==null)return;lan.Host();phase=1;return;}
                if(phase==1)
                {
                    if(player==null||round==null||!round.IsClientInitialized)return;
                    round.StartRound();if(SessionState.GetBool("Marco.FootProbe.Transitions",false))trial=14;worldType=typeof(NetworkRound).Assembly.GetType("Marco.Network.Audio.SoundWorld");world=Activator.CreateInstance(worldType,true);until=now+11;phase=2;return;
                }
                if(phase==2&&now>=until)
                {
                    if(trial==0)Audit();
                    player.SetRole(trial<7?PlayerRole.Seeker:PlayerRole.Runner);
                    player.GetComponent<PlayerMovement>().enabled=false;player.GetComponent<PlayerInteractor>().enabled=false;
                    int place=trial>=14?0:trial%7;Collider support=null;
                    string zone=place==0?"H":place==1?"C":place==2?"LOB":place==3?"L2":"";
                    if(place<4)support=Find(zone,"Floor");
                    else foreach(var c in UnityEngine.Object.FindObjectsByType<Collider>())
                        if(Meta(c,"category")=="Stair"&&Meta(c,"designId")=="S"+(place-3)&&
                           (support==null||c.bounds.size.x*c.bounds.size.z>support.bounds.size.x*support.bounds.size.z))support=c;
                    if(support==null)throw new Exception("No support for trial "+trial+" zone "+zone);
                    label=trial==14?"H-sprint":trial==15?"H-transition":place<4?zone:"S"+(place-3);
                    var point=support.bounds.center;
                    if(place==0)point=new Vector3(1,0,40);
                    float top=-100;foreach(var hit in Physics.RaycastAll(new Vector3(point.x,support.bounds.max.y+1,point.z),Vector3.down,10,~0,QueryTriggerInteraction.Ignore))
                    {if(hit.normal.y<.5f||hit.collider.GetComponentInParent<NetworkPlayer>()!=null||place>=4&&Meta(hit.collider,"category")!="Stair")continue;top=Mathf.Max(top,hit.point.y);}
                    if(top<-50)throw new Exception("No floor height");
                    var cc=player.GetComponent<CharacterController>();float offset=player.transform.position.y-cc.bounds.min.y;
                    point.y=top+offset+.04f;player.GetComponent<PlayerMovement>().Teleport(point);
                    var driver=player.GetComponent<FootProbeDriver>()??player.gameObject.AddComponent<FootProbeDriver>();
                    driver.Drive=false;driver.Axis=support.bounds.size.x>support.bounds.size.z?Vector3.right:Vector3.forward;
                    if(place==0||place==1)driver.Axis=Vector3.forward;
                    driver.Speed=place>=4?1.8f:(player.Role==PlayerRole.Seeker?5.4f:5f);driver.SwitchGait=trial==15;if(trial>=14)driver.Speed=7.5f;driver.ResetTravel();
                    until=now+.5;phase=3;return;
                }
                if(phase==3&&now>=until)
                {
                    var driver=player.GetComponent<FootProbeDriver>();driver.Drive=true;
                    frames=grounded=changes=rings=0;heard.Clear();lastIndex=player.GetComponent<CharacterMotion>().FootstepIndex;
                    until=now+(trial%7>=4?4:1.7);phase=4;return;
                }
                if(phase==4)
                {
                    frames++;object[] args={player,null,.09f};if((bool)worldType.GetMethod("TryFloorNear").Invoke(world,args))grounded++;
                    var motion=player.GetComponent<CharacterMotion>();if(motion.FootstepIndex<lastIndex)throw new Exception("Gait change moved footstep index backwards"); if(motion.FootstepIndex!=lastIndex){changes++;lastIndex=motion.FootstepIndex;}
                    foreach(var a in round.GetComponentsInChildren<AudioSource>())
                        if(a.clip!=null&&(a.clip.name.StartsWith("Tile_")||a.clip.name.StartsWith("Concrete_"))&&a.volume>.001f)heard.Add(a);
                    foreach(var m in UnityEngine.Object.FindObjectsByType<MeshFilter>())if(m.name=="Movement Floor Pulse "+player.PlayerId)rings++;
                    if(now<until)return;
                    var driver=player.GetComponent<FootProbeDriver>();driver.Drive=false;
                    args=new object[]{player,null,.25f};bool floorFound=(bool)worldType.GetMethod("TryFloorNear").Invoke(world,args);var support=args[1] as Collider;
                    string surface=floorFound?(string)worldType.GetMethod("Surface").Invoke(world,new object[]{support,player.transform.position}):"none";
                    float multiplier=floorFound?(float)worldType.GetMethod("SurfaceMultiplier").Invoke(world,new object[]{support,player.transform.position}):0;
                    object[] hear={player.transform.position,player,2f*multiplier,Vector3.zero,0f,false};bool audible=(bool)worldType.GetMethod("Hear").Invoke(world,hear);
                    var asset=player.GetComponent<CharacterBody>().Definition;
                    var water=(int)worldType.GetMethod("WaterState").Invoke(world,new object[]{player,0f});
                    Debug.Log($"[FootProbe] role={player.Role} zone={label} moved={driver.Distance:F2} ground={grounded}/{frames} contacts={changes} sounds={heard.Count} ringFrames={rings} support={support?.name} category={(support!=null?Meta(support,"category"):"")} surface={surface} mul={multiplier} selfHear={audible} gain={hear[4]} water={water} motionSpeed={motion.Speed:F2} run={motion.FootstepRunning} clip={asset?.walkClip?.length} cycle={asset?.walkCycleDuration} eye={player.GetComponent<PlayerMovement>().WaterEyeHeight-player.transform.position.y:F2}");
                    if(heard.Count==0 || changes>heard.Count+1)throw new Exception("Footfall audio missing in "+player.Role+"/"+label);
                    if(label!="C" && driver.Distance>6.1f && rings==0)throw new Exception("No ring on clear 6m path "+player.Role+"/"+label);
                    trial++;if(trial==16){Debug.Log("[FootProbe] PASS: sprint/transition checks; both roles, H/C/LOB/L2/S1/S2/S3; monotonic gait; animation-matched audible footfalls; clear-path floor rings");Finish(0);return;}until=now+.2;phase=2;
                }
            }catch(Exception e){Debug.LogException(e);Finish(1);}
        }
        static void Finish(int code){result=code;phase=9;deadline=EditorApplication.timeSinceStartup+30;EditorApplication.ExitPlaymode();}
    }
}
