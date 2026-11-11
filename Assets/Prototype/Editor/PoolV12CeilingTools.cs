using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class PoolV12CeilingTools
{
    public const string MapScene = "Assets/Scenes/Maps/IndoorPool_v1_2.unity";
    static readonly string Reports = Environment.GetEnvironmentVariable("MARCO_CEILING_REPORT") ?? "Temp/PoolCeilingVerification";
    const string AssemblyName = "ClosedCeilings_StairEnclosures";
    const string PrefabPath = "Assets/Maps/PoolV12/Generated/Prefabs/ClosedCeilingEnvelope.prefab";
    const string DesignPath = "Assets/Maps/PoolV12/Design/Layout_v1_2.json";
    static PoolV12Layout Layout() => JsonUtility.FromJson<PoolV12Layout>(File.ReadAllText(DesignPath));
    static PoolV12Part[] Parts() => UnityEngine.Object.FindObjectsByType<PoolV12Part>(FindObjectsInactive.Include,FindObjectsSortMode.None);
    static bool Inside(PoolV12Room room,float x,float z) => room.rects.Any(r=>r.Contains(x,z));


    public static void Apply()
    {
        Directory.CreateDirectory(Reports);
        var scene=EditorSceneManager.OpenScene(MapScene);
        var before=Snapshots();
        var environment=scene.GetRootGameObjects().Single(g=>g.name=="Environment");
        if(environment.transform.Find(AssemblyName)!=null || File.Exists(PrefabPath))
            throw new InvalidOperationException("Existing ceiling installation found; refusing to overwrite it.");
        Capture(scene.name+"_Before");
        var assembly=Generate();
        assembly.transform.SetParent(environment.transform,false);
        ValidateCurrent();
        Preserve(before,scene.name);
        Capture(scene.name+"_After");
        if(PrefabUtility.SaveAsPrefabAssetAndConnect(assembly,PrefabPath,InteractionMode.AutomatedAction)==null)
            throw new InvalidOperationException("Could not save ceiling prefab.");
        assembly.name=AssemblyName;
        foreach(var view in UnityEngine.Object.FindObjectsByType<PoolV12Viewer>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            view.cutHeight=0;
            view.roofs=assembly;
            EditorUtility.SetDirty(view);
        }
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save pool map.");
        AssetDatabase.SaveAssets();
        Debug.Log("MARCO_V12_CEILING_PASS");
    }
    static void AddHeadroom(GameObject root,PoolV12Layout layout)
    {
        if(root.transform.Find("HeadroomRaised_30cm")!=null) return;
        var group=new GameObject("HeadroomRaised_30cm");group.transform.SetParent(root.transform,false);
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Maps/PoolV12/Materials/Tile.mat");
        foreach(var stair in layout.stairs.Where(s=>s.id=="S1" || s.id=="S2"))
        {
            var r=stair.rect;float x0=r.x0-.5f,x1=r.x1+.5f,z0=r.z0-.5f,z1=r.z1+.5f;
            var oldRoofs=root.GetComponentsInChildren<PoolV12Part>(true).Where(p=>p.category=="Ceiling").ToArray();
            foreach(var roof in oldRoofs)
            {
                var c=roof.GetComponentInChildren<BoxCollider>(true);
                var center=c.transform.TransformPoint(c.center);var size=Vector3.Scale(c.size,c.transform.lossyScale);
                if(Mathf.Abs(center.y-size.y/2-6.5f)>.01f) continue;
                float ax0=center.x-size.x/2,ax1=center.x+size.x/2,az0=center.z-size.z/2,az1=center.z+size.z/2;
                float ix0=Mathf.Max(x0,ax0),ix1=Mathf.Min(x1,ax1),iz0=Mathf.Max(z0,az0),iz1=Mathf.Min(z1,az1);
                if(ix1-ix0<.001f || iz1-iz0<.001f) continue;
                int piece=0;
                void Panel(float left,float bottom,float right,float top,float y,bool raised)
                {
                    if(right-left<.001f || top-bottom<.001f) return;
                    Box(group.transform,stair.id+"_"+roof.name+"_"+piece+++(raised?"_Raised":"_Retained"),new Vector3((left+right)/2,y+.1f,(bottom+top)/2),new Vector3(right-left,.2f,top-bottom),material,"Ceiling");
                }
                Panel(ax0,az0,ix0,az1,6.5f,false);Panel(ix1,az0,ax1,az1,6.5f,false);
                Panel(ix0,az0,ix1,iz0,6.5f,false);Panel(ix0,iz1,ix1,az1,6.5f,false);
                Panel(ix0,iz0,ix1,iz1,6.8f,true);
                UnityEngine.Object.DestroyImmediate(roof.gameObject);
            }
            // Close the 30cm step all around; overlap existing roof thickness for a sealed joint.
            Box(group.transform,stair.id+"_RaisedJoint_W",new Vector3(x0,6.75f,(z0+z1)/2),new Vector3(.2f,.5f,z1-z0),material,"CeilingClosure");
            Box(group.transform,stair.id+"_RaisedJoint_E",new Vector3(x1,6.75f,(z0+z1)/2),new Vector3(.2f,.5f,z1-z0),material,"CeilingClosure");
            Box(group.transform,stair.id+"_RaisedJoint_S",new Vector3((x0+x1)/2,6.75f,z0),new Vector3(x1-x0,.5f,.2f),material,"CeilingClosure");
            Box(group.transform,stair.id+"_RaisedJoint_N",new Vector3((x0+x1)/2,6.75f,z1),new Vector3(x1-x0,.5f,.2f),material,"CeilingClosure");
            var shaft=root.transform.Find(stair.id+"_EnclosedStairway");
            foreach(var part in shaft.GetComponentsInChildren<PoolV12Part>(true))
            {
                var position=part.transform.position;var size=part.designSize;
                if(part.name=="LowerEntryLintel") position.y+=.3f;
                else if(part.name.StartsWith("SideWall_")) {position.y+=.15f;size.y+=.3f;}
                else continue;
                part.transform.position=position;part.designPosition=position;part.designSize=size;
                part.transform.Find("Visual_ReplaceWithFBX").localScale=size;
                part.GetComponentInChildren<BoxCollider>(true).size=size;
            }
        }
    }


    static string TerrainCategory(string category) =>
        category == "StairScreen" || category == "CeilingClosure" ? "Wall" : category;

    public static void ReviseForDesign()
    {
        Directory.CreateDirectory(Reports);
        var scene=EditorSceneManager.OpenScene(MapScene);
        var original=scene.GetRootGameObjects().Single(g=>g.name=="Environment").transform.Find(AssemblyName);
        if(original==null || !PrefabUtility.IsPartOfPrefabInstance(original.gameObject))
            throw new InvalidOperationException("Existing ceiling instance not found.");
        var prefab=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var gallery=prefab.transform.Cast<Transform>().Where(t=>t.name.StartsWith("GallerySightScreen_",StringComparison.Ordinal)).ToArray();
            if(gallery.Length!=4) throw new InvalidOperationException("Unexpected gallery screen count: "+gallery.Length);
            // Validate the candidate in the actual map before writing the prefab.
            var candidate=UnityEngine.Object.Instantiate(prefab);
            candidate.name=AssemblyName;
            candidate.transform.SetParent(original.parent,false);
            original.name=AssemblyName+"_Previous";
            original.gameObject.SetActive(false);
            try
            {
                foreach(var root in new[]{candidate,prefab})
                {
                    foreach(var screen in root.transform.Cast<Transform>().Where(t=>t.name.StartsWith("GallerySightScreen_",StringComparison.Ordinal)).ToArray())
                        UnityEngine.Object.DestroyImmediate(screen.gameObject);
                    foreach(var part in root.GetComponentsInChildren<PoolV12Part>(true))
                        part.category=TerrainCategory(part.category);
                }
                // Refresh per-renderer property blocks after category changes.
                candidate.SetActive(false);candidate.SetActive(true);
                ValidateCurrent();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                original.name=AssemblyName;original.gameObject.SetActive(true);
            }
            if(PrefabUtility.SaveAsPrefabAsset(prefab,PrefabPath)==null)
                throw new InvalidOperationException("Could not save revised ceiling prefab.");
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        // Reopen the saved scene to test imported prefab references; do not resave scene files.
        EditorSceneManager.OpenScene(MapScene);
        ValidateCurrent();
        Capture("IndoorPool_v1_2_DesignRevision");
        Debug.Log("MARCO_CEILING_DESIGN_REVISION_PASS");
    }

    public static void ValidateSavedDesign()
    {
        EditorSceneManager.OpenScene(MapScene);
        ValidateCurrent();
        Debug.Log("MARCO_SAVED_CEILING_WAVE_PASS");
    }

    static string ValidateAcousticOpenings(Transform ceiling,PoolV12Layout layout)
    {
        // Same part classification, deduplication and wall weights as SoundWorld.HearResolved.
        int WallWeight(Vector3 a,Vector3 b)
        {
            var delta=b-a;
            return Physics.RaycastAll(a,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore)
                .Select(h=>h.collider.GetComponentInParent<PoolV12Part>())
                .Where(p=>p!=null && (p.soundOccluder || p.category=="Wall" || p.category=="Floor" || p.category=="Ceiling"))
                .Distinct().Sum(p=>p.category=="Floor" || p.category=="Ceiling" ? 2 : 1);
        }
        var rays=new List<(string name,Vector3 from,Vector3 to,bool slab)>();
        foreach(float x in new[]{16f,20f,27.5f,35f,39f})
            rays.Add(("Hall to gallery",new Vector3(x,1.65f,38),new Vector3(x,5.15f,33.6f),false));
        foreach(var s in layout.stairs)
        {
            var r=s.rect;
            var low=s.alongX?new Vector3(r.x0-.4f,1.65f,(r.z0+r.z1)/2):new Vector3((r.x0+r.x1)/2,1.65f,r.z0-.4f);
            var high=s.alongX?new Vector3(r.x1+.4f,5.15f,(r.z0+r.z1)/2):new Vector3((r.x0+r.x1)/2,5.15f,r.z1+.4f);
            rays.Add((s.id+" open flight",low,high,false));
        }
        rays.Add(("Gallery lobby slab",new Vector3(30,1.65f,20),new Vector3(30,5.15f,20),true));
        rays.Add(("Water tank slab",new Vector3(4.5f,1.65f,21.5f),new Vector3(4.5f,5.15f,21.5f),true));
        var soundType=AppDomain.CurrentDomain.GetAssemblies()
            .Select(a=>a.GetType("Marco.Network.Audio.SoundWorld")).First(t=>t!=null);
        var sound=Activator.CreateInstance(soundType,true);
        var visualReach=soundType.GetMethod("VisualReach");
        float Reach(Vector3 a,Vector3 b)
        {
            var delta=b-a;
            return (float)visualReach.Invoke(sound,new object[]{a,delta.normalized,delta.magnitude});
        }
        foreach(var ray in rays)
        {
            int forward=WallWeight(ray.from,ray.to),reverse=WallWeight(ray.to,ray.from);
            int expected=ray.slab?2:0;
            if(forward!=expected || reverse!=expected)
                throw new Exception($"Acoustic opening {ray.name}: expected {expected}, got {forward}/{reverse}.");
            float distance=Vector3.Distance(ray.from,ray.to);
            bool stops=Reach(ray.from,ray.to)<distance-.01f;
            if(stops!=ray.slab || (Reach(ray.to,ray.from)<distance-.01f)!=ray.slab)
                throw new Exception("Wave reach contradicts opening/slab: "+ray.name);
        }
        var hallProbe=new Vector3(25,5,45);
        if(Mathf.Abs(Reach(hallProbe,hallProbe+Vector3.up*5)-4f)>.02f)
            throw new Exception("Wave ray did not stop at the 9m hall ceiling.");
        foreach(var part in ceiling.GetComponentsInChildren<PoolV12Part>())
        {
            if(part.category!="Ceiling" && part.category!="Wall") throw new Exception("Ceiling piece not classified as clicker terrain: "+part.name);
            foreach(var renderer in part.GetComponentsInChildren<Renderer>())
            {
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                if(block.GetFloat("_MarcoClickerTerrain")<.5f) throw new Exception("Clicker terrain flag missing: "+part.name);
            }
        }
        return "PASS: 16 directed hall/gallery and stair ray samples have no added attenuation; 4 slab ray samples retain wall weight 2 (radius x0.25). Runtime SoundWorld.VisualReach preserves those openings and stops at the two slabs and the 9m hall ceiling. Ceiling and vertical closure renderers are tagged for the terrain flash.\n";
    }

    static GameObject Generate()
    {
        var layout=Layout(); var root=new GameObject(AssemblyName);
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Maps/PoolV12/Materials/Tile.mat");
        if(material==null || !material.shader.isSupported) throw new Exception("Existing microphone-compatible ceiling material is missing.");
        var heights=new float[128,124];
        for(int x=0;x<128;x++) for(int z=0;z<124;z++)
        {
            float px=(x+.5f)*.5f,pz=(z+.5f)*.5f;
            var lower=layout.rooms.Concat(layout.voids).FirstOrDefault(r=>r.floor==1 && Inside(r,px,pz));
            var upper=layout.rooms.FirstOrDefault(r=>r.floor==2 && Inside(r,px,pz));
            float h=lower==null?3.5f:lower.ceiling;
            if(upper!=null) h=Mathf.Max(h,3.5f+upper.ceiling);
            // Half-cell margins keep stepped roof fascia outside the shoulder/head clearance.
            if(layout.stairs.Any(s=>px>=s.rect.x0-.5f && px<s.rect.x1+.5f && pz>=s.rect.z0-.5f && pz<s.rect.z1+.5f)) h=Mathf.Max(h,6.5f);
            heights[x,z]=h;
        }
        var used=new bool[128,124];
        for(int x=0;x<128;x++) for(int z=0;z<124;z++)
        {
            if(used[x,z]) continue;
            float h=heights[x,z];int w=1,d=1;
            while(x+w<128 && !used[x+w,z] && heights[x+w,z]==h) w++;
            while(z+d<124 && Enumerable.Range(x,w).All(a=>!used[a,z+d] && heights[a,z+d]==h)) d++;
            for(int a=x;a<x+w;a++) for(int b=z;b<z+d;b++) used[a,b]=true;
            Box(root.transform,"Ceiling_"+x+"_"+z,new Vector3((x+w/2f)*.5f,h+.1f,(z+d/2f)*.5f),new Vector3(w*.5f,.2f,d*.5f),material,"Ceiling");
        }
        Physics.SyncTransforms();
        var walls=Parts().Where(p=>p.category=="Wall" && p.gameObject.activeInHierarchy).SelectMany(p=>p.GetComponentsInChildren<Collider>()).Select(c=>c.bounds).ToArray();
        for(int axis=0;axis<2;axis++)
        {
            int edges=axis==0?128:124,runs=axis==0?124:128;
            for(int edge=1;edge<edges;edge++)
            {
                var strips=new Dictionary<(float low,float high),List<int>>();
                for(int run=0;run<runs;run++)
                {
                    float a=axis==0?heights[edge-1,run]:heights[run,edge-1],b=axis==0?heights[edge,run]:heights[run,edge];
                    if(a==b) continue;
                    var gaps=new List<(float low,float high)> { (Mathf.Min(a,b)+.2f,Mathf.Max(a,b)+.2f) };
                    float line=edge*.5f,mid=(run+.5f)*.5f;
                    foreach(var wall in walls)
                    {
                        bool hit=axis==0 ? wall.min.x<=line-.099f && wall.max.x>=line+.099f && wall.min.z<mid && wall.max.z>mid : wall.min.z<=line-.099f && wall.max.z>=line+.099f && wall.min.x<mid && wall.max.x>mid;
                        if(!hit) continue;
                        var next=new List<(float low,float high)>();
                        foreach(var gap in gaps)
                        {
                            if(wall.max.y<=gap.low || wall.min.y>=gap.high) next.Add(gap);
                            else
                            {
                                if(wall.min.y>gap.low) next.Add((gap.low,wall.min.y));
                                if(wall.max.y<gap.high) next.Add((wall.max.y,gap.high));
                            }
                        }
                        gaps=next;
                    }
                    foreach(var gap in gaps.Where(g=>g.high-g.low>.001f))
                    {
                        if(!strips.TryGetValue(gap,out var list)) strips[gap]=list=new List<int>();
                        list.Add(run);
                    }
                }
                foreach(var strip in strips) for(int i=0;i<strip.Value.Count;)
                {
                    int start=strip.Value[i++],end=start+1;
                    while(i<strip.Value.Count && strip.Value[i]==end) {end++;i++;}
                    float lo=strip.Key.low,hi=strip.Key.high;
                    var pos=axis==0?new Vector3(edge*.5f,(lo+hi)/2,(start+end)*.25f):new Vector3((start+end)*.25f,(lo+hi)/2,edge*.5f);
                    var size=axis==0?new Vector3(.2f,hi-lo,(end-start)*.5f):new Vector3((end-start)*.5f,hi-lo,.2f);
                    Box(root.transform,"HeightClosure_"+axis+"_"+edge+"_"+start+"_"+lo,pos,size,material,"CeilingClosure");
                }
            }
        }
        foreach(var stair in layout.stairs)
        {
            // The pool-hall emergency stairs retain their original open railings.
            if(stair.id=="S3") continue;
            var r=stair.rect; var shaft=new GameObject(stair.id+"_EnclosedStairway");shaft.transform.SetParent(root.transform,false);
            const float top=6.5f,thickness=.2f,offset=.15f;
            if(stair.alongX)
            {
                foreach(float z in new[]{r.z0-offset,r.z1+offset})
                    Box(shaft.transform,"SideWall_"+z,new Vector3((r.x0+r.x1)/2,top/2,z),new Vector3(r.Width,top,thickness),material,"StairScreen");
                Box(shaft.transform,"LowerEntryLintel",new Vector3(r.x0,4.75f,(r.z0+r.z1)/2),new Vector3(.2f,3.5f,r.Depth),material,"StairScreen");
            }
            else
            {
                foreach(float x in new[]{r.x0-offset,r.x1+offset})
                    Box(shaft.transform,"SideWall_"+x,new Vector3(x,top/2,(r.z0+r.z1)/2),new Vector3(thickness,top,r.Depth),material,"StairScreen");
                Box(shaft.transform,"LowerEntryLintel",new Vector3((r.x0+r.x1)/2,4.75f,r.z0),new Vector3(r.Width,3.5f,.2f),material,"StairScreen");
            }
            // Keep both ends open. This canopy is above the whole rise, never a slab through the flight.
            if(stair.alongX) Box(shaft.transform,"StairCanopy",r.Center(top+.1f),new Vector3(r.Width,.2f,r.Depth+.5f),material,"Ceiling");
        }
        AddHeadroom(root,layout);
        return root;
    }

    static void Box(Transform parent,string name,Vector3 center,Vector3 size,Material material,string category)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=center;
        var visual=GameObject.CreatePrimitive(PrimitiveType.Cube);visual.name="Visual_ReplaceWithFBX";visual.transform.SetParent(go.transform,false);visual.transform.localScale=size;
        UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());visual.GetComponent<Renderer>().sharedMaterial=material;
        var solid=new GameObject("Collision");solid.transform.SetParent(go.transform,false);solid.AddComponent<BoxCollider>().size=size;
        var part=go.AddComponent<PoolV12Part>();part.designId=name;part.displayName=name;part.category=TerrainCategory(category);
        part.designPosition=center;part.designSize=size;part.soundOccluder=true;part.tagBlocker=true;
        part.note="Closed interior; visual and physical collision are separate. Both stair entries remain open. Audio attenuation is provided by the game system.";
    }

    static Dictionary<Transform,string> Snapshots() => UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
        .Where(t=>!t.GetComponentsInParent<Transform>(true).Any(a=>a.name==AssemblyName))
        .ToDictionary(t=>t,t=>t.localPosition.ToString("R")+t.localRotation.ToString("R")+t.localScale.ToString("R")+t.gameObject.activeSelf);
    static void Preserve(Dictionary<Transform,string> before,string scene)
    {
        int count=0;
        foreach(var entry in before)
        {
            if(entry.Key==null) throw new Exception("Existing object was removed");
            var t=entry.Key;
            string now=t.localPosition.ToString("R")+t.localRotation.ToString("R")+t.localScale.ToString("R")+t.gameObject.activeSelf;
            if(entry.Value!=now) throw new Exception("Existing placement or active state changed: "+t.name);
            count++;
        }
        File.WriteAllText(Reports+"/"+scene+"_Preservation.txt","PASS: "+count+" existing objects retain position, rotation, scale and active state.\n");
    }

    [MenuItem("MARCO/Map/Validate closed pool ceilings")]
    public static void ValidateCurrent()
    {
        Directory.CreateDirectory(Reports);
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();var layout=Layout();
        var root=scene.GetRootGameObjects().Single(g=>g.name=="Environment").transform.Find(AssemblyName);
        if(root==null) throw new Exception("Ceiling prefab is missing");
        Physics.SyncTransforms();var solids=root.GetComponentsInChildren<Collider>();
        var roofs=Parts().Where(p=>p.category=="Ceiling" && p.transform.IsChildOf(root)).SelectMany(p=>p.GetComponentsInChildren<Collider>()).ToArray();
        int covered=0,clear=0,screens=0;
        for(int x=0;x<128;x++) for(int z=0;z<124;z++)
        {
            var ray=new Ray(new Vector3((x+.5f)*.5f,-1,(z+.5f)*.5f),Vector3.up);
            if(!roofs.Any(c=>c.Raycast(ray,out var hit,12))) throw new Exception("Roof hole: "+x+","+z);
            covered++;
        }
        if(root.Find("HeadroomRaised_30cm")!=null)
        foreach(var stair in layout.stairs.Where(s=>s.id=="S1" || s.id=="S2"))
        {
            var r=stair.rect;
            foreach(float t in new[]{.1f,.5f,.9f})
            {
                var ray=new Ray(new Vector3((r.x0+r.x1)/2,5.8f,Mathf.Lerp(r.z0,r.z1,t)),Vector3.up);
                var heights=roofs.Select(c=>c.Raycast(ray,out var hit,2)?hit.point.y:float.PositiveInfinity);
                if(Mathf.Abs(heights.Min()-6.8f)>.01f) throw new Exception("Raised headroom not continuous: "+stair.id);
                foreach(float x in new[]{r.x0-.5f,r.x1+.5f})
                    if(!solids.Any(c=>c.Raycast(new Ray(new Vector3(x-.4f,6.85f,Mathf.Lerp(r.z0,r.z1,t)),Vector3.right),out var hit,.8f))) throw new Exception("Raised roof side seam open: "+stair.id);
                foreach(float z in new[]{r.z0-.5f,r.z1+.5f})
                    if(!solids.Any(c=>c.Raycast(new Ray(new Vector3(Mathf.Lerp(r.x0,r.x1,t),6.85f,z-.4f),Vector3.forward),out var hit,.8f))) throw new Exception("Raised roof end seam open: "+stair.id);
            }
        }
        void Free(Vector3 feet,string label)
        {
            // Current players: 2m tall, radius .35m. Allow another .08m radial/.10m height margin.
            const float radius=.43f,height=2.1f;
            var hits=Physics.OverlapCapsule(feet+Vector3.up*(radius+.04f),feet+Vector3.up*(height-radius+.04f),radius,~0,QueryTriggerInteraction.Ignore);
            var blocker=hits.FirstOrDefault(c=>c.transform.IsChildOf(root));
            if(blocker!=null) throw new Exception("New ceiling blocks "+label+" at "+feet+" / "+blocker.transform.parent.name);
            clear++;
        }
        foreach(var stair in layout.stairs)
        {
            var r=stair.rect;
            for(int i=-3;i<=27;i++) for(int lane=-1;lane<=1;lane++)
            {
                float run=i*.25f,t=Mathf.Clamp01(run/stair.run),side=lane*(stair.width/2-.5f);
                var feet=stair.alongX?new Vector3(r.x0+run,t*stair.rise,(r.z0+r.z1)/2+side):new Vector3((r.x0+r.x1)/2+side,t*stair.rise,r.z0+run);
                Free(feet,stair.id+" flight and landings");
            }
            if(stair.id=="S3" && root.Find("S3_EnclosedStairway")!=null) throw new Exception("Pool emergency stair enclosure must remain removed");
            if(stair.id!="S3") for(int i=2;i<=9;i++) foreach(int side in new[]{-1,1})
            {
                float t=i/10f;
                var eye=stair.alongX?new Vector3(Mathf.Lerp(r.x0,r.x1,t),t*stair.rise+1.62f,(r.z0+r.z1)/2):new Vector3((r.x0+r.x1)/2,t*stair.rise+1.62f,Mathf.Lerp(r.z0,r.z1,t));
                var direction=(stair.alongX?Vector3.forward:Vector3.right)*side;
                var ray=new Ray(eye,direction);
                if(!solids.Any(c=>c.Raycast(ray,out var hit,stair.width/2+1))) throw new Exception("Stair side sightline remains open: "+stair.id);
                screens++;
            }
        }
        foreach(var door in Parts().Where(p=>p.category=="Door" || p.category=="ExternalDoor"))
        {
            // Existing corner walls already occupy .10m beyond the mathematical opening edge.
            var pos=door.transform.position; float half=Mathf.Max(0,door.openingWidth/2-.6f);
            foreach(float offset in new[]{-.65f,0,.65f}) foreach(float side in new[]{-half,0,half})
                Free(pos+door.transform.forward*offset+door.transform.right*side,door.designId);
        }
        var spawns=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="SpawnPoints");
        if(spawns!=null) foreach(Transform spawn in spawns.transform) Free(spawn.position,spawn.name);
        foreach(var renderer in root.GetComponentsInChildren<Renderer>()) if(renderer.sharedMaterial==null || !renderer.sharedMaterial.shader.isSupported) throw new Exception("Unsupported ceiling material");
        string acoustics=ValidateAcousticOpenings(root,layout);
        string walks=WalkComparison(root.gameObject,layout);
        string summary=$"PASS: {covered} roof coverage samples; {clear} capsule clearances (2.1m high, radius .43m), including three stairs/landings, all door approaches and spawns; {screens} stair side sightline checks.\n"+walks;
        if(root.Find("HeadroomRaised_30cm")!=null) summary+="PASS: S1/S2 ceiling underside 6.8m at 6 samples; 24 raised roof seam samples sealed. Lower entrance 3.3m.\n";
        summary+=acoustics;
        File.WriteAllText(Reports+"/"+scene.name+"_Validation.txt",summary);Debug.Log(summary);
    }

    static string WalkComparison(GameObject enclosure,PoolV12Layout layout)
    {
        var results=new List<string>();
        foreach(var stair in layout.stairs) foreach(bool descending in new[]{false,true})
        {
            Vector3[] ends=new Vector3[2];
            for(int mode=0;mode<2;mode++)
            {
                enclosure.SetActive(mode==1);Physics.SyncTransforms();
                var obj=new GameObject("Temporary stair clearance walker");
                var cc=obj.AddComponent<CharacterController>();cc.height=2;cc.radius=.35f;cc.center=Vector3.zero;cc.skinWidth=.08f;cc.stepOffset=.3f;cc.slopeLimit=45;cc.minMoveDistance=0;
                var r=stair.rect;float run=descending?stair.run+.5f:-.5f;
                Vector3 feet=stair.alongX?new Vector3(r.x0+run,descending?stair.rise:0,(r.z0+r.z1)/2):new Vector3((r.x0+r.x1)/2,descending?stair.rise:0,r.z0+run);
                obj.transform.position=feet+Vector3.up*1.05f;Physics.SyncTransforms();
                Vector3 direction=(stair.alongX?Vector3.right:Vector3.forward)*(descending?-1:1);
                for(int i=0;i<Mathf.RoundToInt((stair.run+1)/.04f);i++) cc.Move(direction*.04f-Vector3.up*.035f);
                ends[mode]=obj.transform.position;UnityEngine.Object.DestroyImmediate(obj);
            }
            enclosure.SetActive(true);Physics.SyncTransforms();
            if(Vector3.Distance(ends[0],ends[1])>.15f) throw new Exception("CharacterController stair movement regressed: "+stair.id+" "+descending+" before="+ends[0]+" after="+ends[1]);
            var rect=stair.rect;float expected=descending?-.5f:stair.run+.5f;
            float actual=stair.alongX?ends[1].x-rect.x0:ends[1].z-rect.z0;
            if(Mathf.Abs(actual-expected)>.25f) throw new Exception("CharacterController could not traverse stairs: "+stair.id+" descending="+descending+" progress="+actual);
            results.Add("PASS: actual CharacterController "+stair.id+(descending?" descending":" ascending")+" matches baseline and reaches landing.");
        }
        return string.Join("\n",results)+"\n";
    }

    static void Capture(string prefix)
    {
        Shader.SetGlobalFloat("_PoolV12CutHeight",0);Shader.SetGlobalFloat("_MarcoMinusEnabled",0);
        var camera=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault();
        if(camera==null) return;
        var obj=new GameObject("CeilingVerificationCamera");var c=obj.AddComponent<Camera>();c.CopyFrom(camera);
        c.enabled=false;c.orthographic=false;c.cullingMask=~0;c.nearClipPlane=.05f;c.farClipPlane=200;c.fieldOfView=75;
        var data=c.GetUniversalAdditionalCameraData();
        var source=camera.GetUniversalAdditionalCameraData();
        data.SetRenderer(new SerializedObject(source).FindProperty("m_RendererIndex").intValue);
        var rt=new RenderTexture(1280,720,24);c.targetTexture=rt;
        void Shot(string name,Vector3 pos,Vector3 angles)
        {
            c.transform.SetPositionAndRotation(pos,Quaternion.Euler(angles));c.Render();var last=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
            File.WriteAllBytes(Reports+"/"+prefix+"_"+name+".png",image.EncodeToPNG());RenderTexture.active=last;UnityEngine.Object.DestroyImmediate(image);
        }
        Shot("Overview",new Vector3(75,75,-30),new Vector3(45,-35,0));
        Shot("Lobby",new Vector3(32,1.62f,3),new Vector3(-10,-55,0));
        Shot("PoolHall",new Vector3(12,1.62f,38),new Vector3(-10,45,0));
        Shot("Gallery",new Vector3(27.5f,1.65f,38f),new Vector3(-28,180,0));
        Shot("S1Side",new Vector3(27,3.72f,13.6f),new Vector3(0,-90,0));
        Shot("S1Up",new Vector3(27,3.02f,12.4f),new Vector3(-12,0,0));
        Shot("S2Up",new Vector3(54.5f,3.02f,12.9f),new Vector3(-12,0,0));
        Shot("S3Up",new Vector3(11.4f,3.02f,32),new Vector3(-12,90,0));
        c.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(obj);
    }
}
