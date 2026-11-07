using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Character;
using Marco.Network.Audio;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace Marco.Network.Editor
{
    [DefaultExecutionOrder(200)] public sealed class SwimmingCameraSampler : MonoBehaviour
    {
        public Action Sample;
        private void LateUpdate()=>Sample?.Invoke();
    }
    [InitializeOnLoad] public static class SwimmingRegressionChecks
    {
        const string Pending="Marco.SwimmingRegression",Scene="Assets/Scenes/Network/NetworkPool_v1_2.unity";
        const string Output="C:/Users/user/.codex/visualizations/2026/10/23/01a16b07-3d73-7d53-b62b-2e9c24c7e28c/sound-integration/current/swim-fixed";
        static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static int phase,result,roleTrial,iteration,lastFrame=-1,transitions,audioFrames;
        static double deadline,until;static float rigReadyAt;static bool previous;
        static float audioMin=1,audioMax;static readonly HashSet<AudioSource> audioSources=new();
        static readonly HumanBodyBones[] Chain={HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot};
        static float[] lengths=new float[8];static float maxLengthError,maxJointAngle;static float lastCameraY,maxCameraStep,maxCameraRate;static int lastCameraFrame=-1,cameraSamples;static RenderTexture cameraTarget;static Camera sampledCamera;
        static SwimmingRegressionChecks(){if(SessionState.GetBool(Pending,false))Subscribe();}
        static void Subscribe(){deadline=EditorApplication.timeSinceStartup+270;EditorApplication.update-=Tick;EditorApplication.update+=Tick;RenderPipelineManager.endCameraRendering-=RenderedCamera;RenderPipelineManager.endCameraRendering+=RenderedCamera;}
        public static void Run()
        {
            if(!Application.isBatchMode)throw new Exception("Batch only");
            EditorSceneManager.OpenScene(Scene);SessionState.SetBool(Pending,true);Subscribe();EditorApplication.EnterPlaymode();
        }
        static void Require(bool value,string message){if(!value)throw new Exception(message);}
        static object Get(object obj,string field)=>obj.GetType().GetField(field,Flags).GetValue(obj);
        static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,Flags).SetValue(obj,value);
        static void Water(PlayerMovement movement)=>typeof(PlayerMovement).GetMethod("UpdateWaterPresentation",Flags).Invoke(movement,null);
        static void RecordLengths(CharacterBody body)
        {for(int i=0;i<4;i++){lengths[i*2]=Vector3.Distance(body.ResolveBone(Chain[i*3]).position,body.ResolveBone(Chain[i*3+1]).position);lengths[i*2+1]=Vector3.Distance(body.ResolveBone(Chain[i*3+1]).position,body.ResolveBone(Chain[i*3+2]).position);}}
        static void CheckPose(CharacterBody body)
        {
            for(int i=0;i<4;i++)
            {
                Vector3 a=body.ResolveBone(Chain[i*3]).position,b=body.ResolveBone(Chain[i*3+1]).position,c=body.ResolveBone(Chain[i*3+2]).position;
                maxLengthError=Mathf.Max(maxLengthError,Mathf.Abs(Vector3.Distance(a,b)-lengths[i*2]),Mathf.Abs(Vector3.Distance(b,c)-lengths[i*2+1]));
                maxJointAngle=Mathf.Max(maxJointAngle,Vector3.Angle(b-a,c-b));
                Require(maxLengthError<.015f,"Swim pose stretched a bone by "+maxLengthError);
                Require(Vector3.Angle(b-a,c-b)<150,"Swim pose folded a limb backwards");
            }
            var chest=body.ResolveBone(HumanBodyBones.Chest);Vector3 center=chest!=null?chest.position:body.ResolveBone(HumanBodyBones.Hips).position;
            float left=Vector3.Dot(body.ResolveBone(HumanBodyBones.LeftHand).position-center,body.BodyRoot.right);
            float right=Vector3.Dot(body.ResolveBone(HumanBodyBones.RightHand).position-center,body.BodyRoot.right);
            Require(left<.08f&&right>-.08f,"Runner hands crossed the torso");
        }
        static void Capture(NetworkPlayer player,string label)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null){Debug.Log("[SwimRegression] No GPU, skipped image "+label);return;}
            Directory.CreateDirectory(Output);
            player.GetComponentInChildren<MicrophoneVisionController>()?.SetDevelopmentFullVision(true);
            var body=player.GetComponent<CharacterBody>();var layers=new Dictionary<GameObject,int>();
            foreach(var r in body.BodyRoot.GetComponentsInChildren<Renderer>()){if(!layers.ContainsKey(r.gameObject))layers[r.gameObject]=r.gameObject.layer;r.gameObject.layer=31;}
            var camera=new GameObject("Swim QA camera").AddComponent<Camera>();camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.17f,.19f);camera.cullingMask=1<<31;
            camera.orthographic=true;camera.orthographicSize=1.25f;camera.nearClipPlane=.02f;camera.farClipPlane=20;
            Vector3 target=(body.ResolveBone(HumanBodyBones.Head).position+body.ResolveBone(HumanBodyBones.Hips).position)*.5f;
            camera.transform.position=target+player.transform.TransformDirection(new Vector3(2,.7f,-3));camera.transform.LookAt(target);
            var type=Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if(type!=null){var data=camera.gameObject.AddComponent(type);type.GetMethod("SetRenderer").Invoke(data,new object[]{1});}
            var rt=new RenderTexture(768,768,24);camera.targetTexture=rt;var previousRT=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=rt;var image=new Texture2D(768,768,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,768,768),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,label+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            }
            finally{RenderTexture.active=previousRT;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camera.gameObject);foreach(var pair in layers)pair.Key.layer=pair.Value;}
        }
        static void RenderedCamera(ScriptableRenderContext context,Camera camera)
        {
            var local=NetworkPlayer.Local;
            if(phase!=4||local==null||camera!=local.GetComponentInChildren<Camera>()||Time.frameCount==lastCameraFrame)return;
            if(iteration==0){lastCameraY=camera.transform.position.y;lastCameraFrame=-1;return;}
            lastCameraFrame=Time.frameCount;cameraSamples++;
            float y=camera.transform.position.y,delta=Mathf.Abs(y-lastCameraY);
            maxCameraStep=Mathf.Max(maxCameraStep,delta);
            float rate=delta/Mathf.Max(.001f,Time.deltaTime);
            if(rate>30)Debug.Log($"[SwimCameraTrace] role={local.Role} iteration={iteration} from={lastCameraY} to={y} dt={Time.deltaTime} weight={Get(local.GetComponent<FirstPersonBody>(),"waterViewWeight")} swimming={local.GetComponent<PlayerMovement>().IsSwimming}");
            maxCameraRate=Mathf.Max(maxCameraRate,rate);lastCameraY=y;
        }
        static void Tick()
        {
            try
            {
                double now=EditorApplication.timeSinceStartup;if(now>deadline)throw new Exception("Timeout phase "+phase);
                if(phase==20)
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                    EditorSceneManager.OpenScene(Scene);EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene);
                    SessionState.SetBool(Pending,false);EditorApplication.update-=Tick;RenderPipelineManager.endCameraRendering-=RenderedCamera;EditorApplication.Exit(result);return;
                }
                if(!EditorApplication.isPlaying||Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
                var round=NetworkRound.Instance;var player=NetworkPlayer.Local;
                if(phase==0){var lan=UnityEngine.Object.FindAnyObjectByType<LanConnector>();if(lan==null)return;lan.Host();phase=1;return;}
                if(phase==1){if(player==null||round==null||!round.IsClientInitialized)return;round.StartRound();until=now+11;phase=2;return;}
                var move=player.GetComponent<PlayerMovement>();var cc=player.GetComponent<CharacterController>();
                var body=player.GetComponent<CharacterBody>();
                if(phase==2&&now>=until)
                {
                    player.SetRole(roleTrial==0?PlayerRole.Runner:PlayerRole.Seeker);move.enabled=false;player.GetComponent<PlayerInteractor>().enabled=false;
                    Set(move,"swimming",false);cc.height=(float)Get(move,"standingHeight");cc.center=(Vector3)Get(move,"standingCenter");
                    move.Teleport(new Vector3(20,.94f,47));rigReadyAt=Time.time+.5f;until=now+.3;phase=3;return;
                }
                if(phase==3&&now>=until&&Time.time>=rigReadyAt)
                {
                    RecordLengths(body);previous=false;transitions=0;iteration=0;lastCameraY=player.GetComponentInChildren<Camera>().transform.position.y;maxCameraStep=maxCameraRate=0;lastCameraFrame=-1;cameraSamples=0; sampledCamera=player.GetComponentInChildren<Camera>();cameraTarget=new RenderTexture(256,256,24);cameraTarget.Create();sampledCamera.targetTexture=cameraTarget;
                    var boundaryDriver=player.GetComponent<FootProbeDriver>()??player.gameObject.AddComponent<FootProbeDriver>();
                    boundaryDriver.Drive=false;boundaryDriver.Observe=()=>Water(move);
                    var sampler=player.GetComponent<SwimmingCameraSampler>()??player.gameObject.AddComponent<SwimmingCameraSampler>();
                    sampler.Sample=()=>
                    {
                        if(phase!=4)return;
                        iteration++;sampledCamera.Render();RenderedCamera(default,sampledCamera);
                        if(move.IsSwimming!=previous)transitions++;previous=move.IsSwimming;
                    };
                    phase=4;return;
                }
                if(phase==4)
                {
                    if(iteration<40)return;
                    Require(transitions==1,"Entry toggled more than once");
                    Require(cameraSamples>0,"No player camera render samples");
                    Require(maxCameraRate<30,"Rendered camera snapped on water entry: "+maxCameraRate);
                    Debug.Log($"[SwimRegression] {player.Role}: fixed boundary 40 frames, transitions={transitions}, height={cc.height}, maxCameraStep={maxCameraStep:F4}, maxCameraRate={maxCameraRate:F3}, renderSamples={cameraSamples}");
                    player.GetComponent<FootProbeDriver>().Observe=null;player.GetComponent<SwimmingCameraSampler>().Sample=null;ReleaseCamera();iteration=0;phase=5;return;
                }
                if(phase==5)
                {
                    float[] jitter={1.00f,1.02f,.98f,1.04f,.99f};
                    move.Teleport(new Vector3(20,jitter[iteration%jitter.Length],47));Water(move);
                    Require(move.IsSwimming,"Contact jitter restarted standing pose");
                    if(++iteration<15)return;
                    move.Teleport(new Vector3(20,1.08f,47));Water(move);Require(!move.IsSwimming,"Swimming did not end above exit threshold");
                    move.Teleport(new Vector3(20,1f,47));Water(move);Require(!move.IsSwimming,"Dry state re-entered inside hysteresis band");
                    if(roleTrial++==0){until=now;phase=2;return;}
                    player.SetRole(PlayerRole.Runner);move.Teleport(new Vector3(20,-.1f,47));Water(move);until=now+.5;phase=6;return;
                }
                if(phase==6&&now>=until)
                {
                    RecordLengths(body);maxLengthError=maxJointAngle=0;Capture(player,"runner-surface-idle");
                    var driver=player.GetComponent<FootProbeDriver>()??player.gameObject.AddComponent<FootProbeDriver>();
                    driver.Axis=Vector3.right;driver.Speed=3;driver.Gravity=0;driver.SwitchGait=false;driver.ResetTravel();driver.Drive=true;
                    move.enabled=true;until=now+.8;phase=7;return;
                }
                if(phase==7)
                {
                    Require(move.IsSwimming&&!move.IsDiving,"Surface swimmer state unstable");CheckPose(body);
                    if(now<until)return;
                    audioSources.Clear();audioFrames=0;audioMin=1;audioMax=0;until=now+4;phase=8;return;
                }
                if(phase==8)
                {
                    Require(move.IsSwimming&&!move.IsDiving,"Surface movement became dry/diving");CheckPose(body);
                    bool heard=false;
                    foreach(var source in round.GetComponentsInChildren<AudioSource>())if(!source.loop&&source.clip!=null&&source.clip.name.StartsWith("SurfaceSwim"))
                    {audioSources.Add(source);audioMin=Mathf.Min(audioMin,source.volume);audioMax=Mathf.Max(audioMax,source.volume);heard=source.volume>.01f;}
                    if (heard) audioFrames++;
                    if(now<until)return;
                    Require(audioSources.Count>=1 && audioFrames>0,"No timed surface audio while moving");
                    Debug.Log($"[SwimRegression] Runner timed surface cues: sources={audioSources.Count}, frames={audioFrames}, volume={audioMin:F3}..{audioMax:F3}, lengthError={maxLengthError:F6}, maxJointAngle={maxJointAngle:F2}");
                    Capture(player,"runner-surface-moving");player.GetComponent<FootProbeDriver>().Drive=false;until=now+.6;phase=9;return;
                }
                if(phase==9&&now>=until)
                {
                    foreach(var source in round.GetComponentsInChildren<AudioSource>())Require(source.clip==null||!source.clip.name.StartsWith("SurfaceSwim"),"Surface loop did not fade after stop");
                    move.enabled=false;move.Teleport(new Vector3(20,-.8f,47));Water(move);Require(move.IsDiving&&move.WaterStateAtSurface(0)==2,"Dive state disagreed with audio classification");
                    round.GetComponent<RoundAudioPlayback>().Play("Valve",player.transform.position,.3f,.5f,-1,false,-1,true);
                    foreach(var filter in round.GetComponentsInChildren<AudioLowPassFilter>())if(filter.GetComponent<AudioSource>()?.clip?.name=="Valve")Require(filter.enabled&&Mathf.Abs(filter.cutoffFrequency-650)<1,"Dive filter overwritten on Play");
                    until=now+.4;phase=10;return;
                }
                if(phase==10)
                {
                    CheckPose(body);if(now<until)return;Capture(player,"runner-diving");
                    move.Teleport(new Vector3(1,1.06f,40));Water(move);Require(!move.IsSwimming,"Leaving pool did not restore standing state");until=now+.5;phase=11;return;
                }
                if(phase==11&&now>=until)
                {
                    Require(Mathf.Abs(cc.height-(float)Get(move,"standingHeight"))<.001f,"Standing collider did not recover");
                    Require(Quaternion.Angle(body.BodyRoot.localRotation,Quaternion.identity)<.05f,"Swim lean remained after exit");
                    Capture(player,"runner-after-exit");
                    Debug.Log("[SwimRegression] PASS: both-role entry hysteresis; timed surface cues; dive filter; runner limb constraints; stop fade; standing recovery; network scene restored");Finish(0);
                }
            }catch(Exception error){Debug.LogException(error);Finish(1);}
        }
        static void ReleaseCamera(){if(sampledCamera!=null)sampledCamera.targetTexture=null;if(cameraTarget!=null){cameraTarget.Release();UnityEngine.Object.DestroyImmediate(cameraTarget);}cameraTarget=null;sampledCamera=null;}
        static void Finish(int code){ReleaseCamera();result=code;phase=20;deadline=EditorApplication.timeSinceStartup+40;EditorApplication.ExitPlaymode();}
    }
}
