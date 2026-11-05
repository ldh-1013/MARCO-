using System;
using System.Reflection;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    public static class MovementPulseChecks
    {
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch inspection only; does not run inside the user's editor.");
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            int exitCode = 0;
            try
            {
                Require(MovementPulseClock.IsFastMovement(Marco.Core.WinCondition.PlayerRole.Seeker,5.4f,false),"Seeker chase above 5m/s must use the fast grade");
                Require(!MovementPulseClock.IsFastMovement(Marco.Core.WinCondition.PlayerRole.Seeker,3.5f,false),"Seeker stealth must use the walk grade");
                Require(MovementPulseClock.IsFastMovement(Marco.Core.WinCondition.PlayerRole.Runner,7.5f,false),"Runner sprint must use the fast grade");
                Require(MovementPulseClock.IsFastMovement(Marco.Core.WinCondition.PlayerRole.Seeker,3f,true),"Surface water keeps its existing fast mark");
                var clock = new MovementPulseClock();
                var position = Vector3.zero;
                clock.Advance(position,.02f,true,false,out _);
                int steps=0,rings=0;
                for(int i=0;i<120;i++)
                {
                    position.x+=.05f;
                    if(clock.Advance(position,.01f,true,false,out bool ring))steps++;
                    if(ring)rings++;
                }
                Require(steps==3&&rings==1,"Walk 6m: expected 3 sounds and 1 ring, got "+steps+"/"+rings);
                clock.Advance(position,.02f,true,false,out _);
                for(int i=0;i<40;i++) {position.x+=.05f;clock.Advance(position,.01f,true,false,out bool ring);Require(!ring,"Walk stop must reset ring counter");}
                clock.Advance(position+Vector3.right*30,.02f,true,true,out bool teleport);
                Require(!teleport,"Teleport emitted a ring");
                clock=new MovementPulseClock();position=Vector3.zero;
                clock.Advance(position,.01f,true,true,out _);steps=0;rings=0;
                for(int i=0;i<80;i++){position.x+=.075f;if(clock.Advance(position,.01f,true,true,out bool ring))steps++;if(ring)rings++;}
                Require(steps==1&&rings==1,"Sprint 6m cadence");

                // Threshold jitter must not erase the distance needed to draw a ring.
                clock=new MovementPulseClock();position=Vector3.zero;rings=0;
                clock.Advance(position,.01f,true,false,out _);
                for(int i=0;i<130;i++)
                {
                    float speed=i%2==0?5f:5.02f;
                    position.x+=speed*.01f;
                    clock.Advance(position,.01f,true,speed>5.01f,out bool ring);
                    if(ring)rings++;
                }
                Require(rings==1&&!clock.Fast,"Threshold jitter erased a ring or changed its grade");
                // A duplicated network position must not discard distance; a real stop must.
                clock=new MovementPulseClock();position=Vector3.zero;rings=0;
                clock.Advance(position,.01f,true,false,out _);
                for(int i=0;i<120;i++)
                {
                    if(i==60)clock.Advance(position,.1f,true,false,out _);
                    position.x+=.05f;clock.Advance(position,.01f,true,false,out bool ring);if(ring)rings++;
                }
                Require(rings==1,"A repeated network sample erased the ring counter");
                clock=new MovementPulseClock();position=Vector3.zero;rings=0;
                clock.Advance(position,.01f,true,false,out _);
                for(int i=0;i<60;i++){position.x+=.05f;clock.Advance(position,.01f,true,false,out _);}
                clock.Advance(position,.2f,true,false,out _);
                for(int i=0;i<60;i++){position.x+=.05f;clock.Advance(position,.01f,true,false,out bool ring);if(ring)rings++;}
                Require(rings==0,"A real stop did not reset cadence");
                // A sustained change starts a fresh six-metre ring cadence.
                clock=new MovementPulseClock();position=Vector3.zero;rings=0;
                clock.Advance(position,.01f,true,false,out _);
                for(int i=0;i<120;i++){bool fast=i>=60;position.x+=.05f;clock.Advance(position,.01f,true,fast,out bool ring);if(ring)rings++;}
                Require(rings==0&&clock.Fast,"A sustained grade change did not reset the ring counter");

                EditorSceneManager.OpenScene("Assets/Scenes/Maps/IndoorPool_v1_2.unity",OpenSceneMode.Single);
                var assembly=typeof(WalkingFootRings).Assembly;
                var worldType=assembly.GetType("Marco.Network.Audio.SoundWorld",true);
                var world=Activator.CreateInstance(worldType,true);
                var reach=worldType.GetMethod("VisualReach");
                MonoBehaviour mapPart=null;
                foreach(var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                    if(component!=null&&component.GetType().Name=="PoolV12Part"){mapPart=component;break;}
                Require(mapPart!=null,"Pool metadata missing");
                // Isolate a wall with a one-metre doorway high above the existing map.
                var wall=new GameObject("Pulse check wall"); wall.transform.position=new Vector3(0,100,0);
                var metadata=wall.AddComponent(mapPart.GetType());
                mapPart.GetType().GetField("category").SetValue(metadata,"Wall");
                var box=wall.AddComponent<BoxCollider>();box.size=new Vector3(.2f,3,4);
                Physics.SyncTransforms();
                var origin=new Vector3(-1,100,0);
                float closed=(float)reach.Invoke(world,new object[]{origin,Vector3.right,5f});
                Require(closed<1.01f,"Wall did not clip floor ring");
                var visualObject=new GameObject("Pulse mesh check");
                var visual=visualObject.AddComponent<WalkingFootRings>(); visual.enabled=false;
                var build=typeof(WalkingFootRings).GetMethod("BuildMesh",BindingFlags.Instance|BindingFlags.NonPublic);
                var mesh=(Mesh)build.Invoke(visual,new object[]{origin,5f,false});
                Require(mesh.vertexCount==130,"Unexpected floor mesh topology");
                Require(mesh.vertices[1].x<1.01f,"Ring crossed closed wall");
                UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(visualObject);
                box.enabled=false;Physics.SyncTransforms();
                float open=(float)reach.Invoke(world,new object[]{origin,Vector3.right,5f});
                Require(Mathf.Abs(open-5)<.01f,"Open doorway did not preserve remaining radius");
                var listenerObject=new GameObject("Pulse hearing check");
                var listener=listenerObject.AddComponent<Marco.Network.Player.NetworkPlayer>();
                listenerObject.transform.position=new Vector3(3,100,0);
                var hear=worldType.GetMethod("Hear");
                object[] arguments={origin,listener,9f,Vector3.zero,0f,false};
                box.enabled=true;Physics.SyncTransforms();
                Require((bool)hear.Invoke(world,arguments),"Talk-grade range behind one wall should be 4.5m");
                Require((bool)arguments[5]&&Mathf.Abs((float)arguments[4]-.5f)<.001f,"Blocked sound must have fixed half gain");
                Require((Vector3)arguments[3]!=origin,"Blocked payload leaked the source position");
                listenerObject.transform.position=new Vector3(4,100,0);
                Require(!(bool)hear.Invoke(world,arguments),"One-wall sound leaked beyond reduced radius");
                box.enabled=false;Physics.SyncTransforms();
                arguments[2]=2f;
                listenerObject.transform.position=origin+Vector3.right*2.2f;
                Require(!(bool)hear.Invoke(world,arguments),"Runner heard walking beyond 2m");
                var role=typeof(Marco.Network.Player.NetworkPlayer).GetField("role",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(listener);
                role.GetType().GetField("_value",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(role,Marco.Core.WinCondition.PlayerRole.Seeker);
                Require((bool)hear.Invoke(world,arguments),"Seeker did not receive its 1.2 hearing multiplier");
                UnityEngine.Object.DestroyImmediate(listenerObject);
                UnityEngine.Object.DestroyImmediate(wall);
                Debug.Log("[MovementPulseChecks] PASS: walk 2m sounds/6m ring; sprint 6m; stop reset; teleport suppression; wall clip; open gap; hearing cutoff; blocked gain/redacted position; Seeker 1.2 hearing. No scene saved.");
            }
            catch(Exception ex){Debug.LogException(ex);exitCode = 1;}
            finally
            {
                if (Array.Exists(previousSetup, scene => scene.isLoaded) && Array.TrueForAll(previousSetup, scene => !string.IsNullOrEmpty(scene.path))) EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
            EditorApplication.Exit(exitCode);
        }
        private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    }
}
