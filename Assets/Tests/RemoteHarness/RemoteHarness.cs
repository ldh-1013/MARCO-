#if MARCO_REMOTE_TEST
// Remote verification harness (2026-11-08 · 파문 원격 전면 검증). Compiled only into test builds that pass
// MARCO_REMOTE_TEST through BuildPlayerOptions.extraScriptingDefines; normal builds contain none of this file.
// It never changes game code: it drives the game through public server methods, reflection and the debug cheats,
// and observes through reflection (ring renderer, round audio voices, voice queues, wrapped RPC receive tables).
// Ripple-specific types are reached by name only, so the same file also compiles into the pre-ripple build (B0).
//
// Command line:  -harness <scenario> -hrole host|client -hid <n> -hout <dir> -hsync <dir> [-hplayers N] [-hsteps a,b]
//                [-hint on|off] [-hrounds N] [-hminutes M]
// Every instance writes <hout>/<scenario>_<role><hid>.jsonl (one JSON object per line) and screenshots.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Delegating;
using FishNet.Serializing;
using FishNet.Transporting;
using Marco.Core.WinCondition;
using Marco.Network;
using Marco.Network.Audio;
using Marco.Network.Escape;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Valves;
using Marco.Network.Vision;
using Marco.Network.Voice;
using UnityEngine;

namespace Marco.Tests.Remote
{
    public sealed class RemoteHarness : MonoBehaviour
    {
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static string scenario, role = "host", outDir = ".", syncDir = ".";
        static int hid, expectedPlayers = 2, rounds = 3;
        static float minutes = 30;
        static bool hintOn;
        static HashSet<string> steps = new();
        static StreamWriter log;
        static RemoteHarness instance;
        static string Who => role + hid;
        static bool IsHost => role == "host";

        // ---------------------------------------------------------------- boot
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
            {
                switch (a[i])
                {
                    case "-harness": scenario = a[i + 1]; break;
                    case "-hrole": role = a[i + 1]; break;
                    case "-hid": int.TryParse(a[i + 1], out hid); break;
                    case "-hout": outDir = a[i + 1]; break;
                    case "-hsync": syncDir = a[i + 1]; break;
                    case "-hplayers": int.TryParse(a[i + 1], out expectedPlayers); break;
                    case "-hrounds": int.TryParse(a[i + 1], out rounds); break;
                    case "-hminutes": float.TryParse(a[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out minutes); break;
                    case "-hsteps": foreach (var s in a[i + 1].Split(',')) steps.Add(s.Trim()); break;
                    case "-hint": hintOn = a[i + 1] == "on"; break;
                }
            }
            if (string.IsNullOrEmpty(scenario)) return;
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(syncDir);
            log = new StreamWriter(Path.Combine(outDir, $"{scenario}_{Who}.jsonl"), true) { AutoFlush = true };
            var go = new GameObject("Remote Harness");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<RemoteHarness>();
            Ev("boot", $"\"scenario\":{Q(scenario)},\"build\":{Q(BuildTag())},\"hint\":{B(hintOn)},\"switches\":{Q(Switches())},\"steps\":{Q(string.Join(",", steps))}");
        }

        static string BuildTag() => T("Marco.Network.Round.RippleServerFeatures") != null ? "ripple" : "pre-ripple";

        static string Switches()
        {
            var parts = new List<string>();
            foreach (var name in new[] { "Marco.Network.Round.RippleServerFeatures", "Marco.Network.Vision.RippleClientFeatures" })
            {
                var t = T(name);
                if (t == null) continue;
                foreach (var f in t.GetFields(BF)) if (f.FieldType == typeof(bool)) parts.Add(f.Name + "=" + f.GetValue(null));
            }
            return string.Join(" ", parts);
        }

        // ---------------------------------------------------------------- JSON log
        static long Ms => DateTimeOffset.Now.ToUnixTimeMilliseconds();
        static void Ev(string ev, string body = null)
        {
            if (log == null) return;
            try { log.WriteLine($"{{\"ms\":{Ms},\"t\":{Q(DateTime.Now.ToString("HH:mm:ss.fff"))},\"who\":{Q(Who)},\"ev\":{Q(ev)}{(body != null ? "," + body : "")}}}"); }
            catch { }
        }
        static string Q(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ") + "\"";
        static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("0.####", CultureInfo.InvariantCulture);
        static string B(bool v) => v ? "true" : "false";
        static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";

        // ---------------------------------------------------------------- reflection
        static readonly Dictionary<string, Type> typeCache = new();
        static Type T(string name)
        {
            if (typeCache.TryGetValue(name, out var cached)) return cached;
            Type found = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { found = asm.GetType(name); if (found != null) break; }
            typeCache[name] = found;
            return found;
        }
        static object Get(object o, string name)
        {
            if (o == null) return null;
            var t = o as Type ?? o.GetType();
            var target = o is Type ? null : o;
            for (var c = t; c != null; c = c.BaseType)
            {
                var f = c.GetField(name, BF); if (f != null) return f.GetValue(target);
                var p = c.GetProperty(name, BF); if (p != null) return p.GetValue(target);
            }
            return null;
        }
        static bool Set(object o, string name, object value)
        {
            if (o == null) return false;
            var t = o as Type ?? o.GetType();
            var target = o is Type ? null : o;
            for (var c = t; c != null; c = c.BaseType)
            {
                var f = c.GetField(name, BF); if (f != null) { f.SetValue(target, value); return true; }
                var p = c.GetProperty(name, BF); if (p != null && p.CanWrite) { p.SetValue(target, value); return true; }
            }
            return false;
        }
        static MethodInfo M(object o, string name, int args)
        {
            var t = o as Type ?? o.GetType();
            for (var c = t; c != null; c = c.BaseType)
                foreach (var m in c.GetMethods(BF | BindingFlags.DeclaredOnly))
                    if (m.Name == name && m.GetParameters().Length == args) return m;
            return null;
        }
        static bool Has(object o, string name, int args) => M(o, name, args) != null;
        static bool Bo(object o) => o is bool b && b;
        static float Fo(object o, float fallback = -1f) => o is float f ? f : fallback;
        static object Call(object o, string name, params object[] args)
        {
            var m = M(o, name, args.Length);
            if (m == null) throw new MissingMethodException(name);
            return m.Invoke(o is Type ? null : o, args);
        }

        // ---------------------------------------------------------------- observers (every instance)
        readonly HashSet<object> seenRings = new(), ringNow = new();
        readonly Dictionary<object, int> seenVoices = new();
        readonly Dictionary<NetworkPlayer, int> queueWrite = new();
        readonly Dictionary<NetworkPlayer, int> packetsThisSecond = new();
        readonly Dictionary<NetworkPlayer, bool> lastBlocked = new();
        readonly Dictionary<string, int> rpcThisSecond = new(), warnKinds = new();
        readonly HashSet<NetworkBehaviour> wrapped = new();
        readonly List<float> frames = new();
        float secondStart, lastMemory;
        int errors, warnings, heard, lastShown = -1, harnessErrors;
        float nextWrap;
        readonly HashSet<Delegate> wrappers = new();
        LanConnector lan;
        LanConnectionStatus lastStatus = LanConnectionStatus.Offline;
        int lastPlayers = -1;
        static readonly List<(long ms, string name, string obj)> rpcArrivals = new();

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            var activity = T("Marco.Network.Lobby.LobbyVoiceActivity");
            var e = activity?.GetEvent("Heard", BF);
            if (e != null) e.AddMethod.Invoke(null, new object[] { (Action<float>)(level => heard++) });
        }

        void Start()
        {
            RippleSwitch(hintOn);
            StartCoroutine(Run());
        }

        static void RippleSwitch(bool on)
        {
            var t = T("Marco.Network.Vision.RippleClientFeatures");
            if (t != null) Set(t, "DirectionHint", on);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            string first = message.Split('\n')[0];
            if (first.Length > 220) first = first.Substring(0, 220);
            string where = "";
            foreach (var line in (stack ?? "").Split('\n')) if (line.StartsWith("Marco.") && !line.StartsWith("Marco.Tests")) { where = line.Trim(); break; }
            if (type == LogType.Warning)
            {
                warnings++;
                string key = first + " @ " + where;
                warnKinds[key] = warnKinds.TryGetValue(key, out int n) ? n + 1 : 1;
                if (warnKinds[key] <= 3) Ev("warning", $"\"msg\":{Q(first)},\"at\":{Q(where)}");
                return;
            }
            errors++;
            if (errors <= 60) Ev(type == LogType.Exception ? "exception" : "error", $"\"msg\":{Q(first)},\"at\":{Q(where)}");
        }

        void Update()
        {
            try
            {
                StopMicrophone();
                WatchConnection();
                WrapRpcs();
                WatchRings();
                WatchSounds();
                WatchVoice();
                WatchHints();
                Frames();
            }
            catch (Exception ex) { if (harnessErrors++ < 20) Ev("harnessError", $"\"msg\":{Q(ex.GetType().Name + ": " + ex.Message)},\"at\":{Q((ex.StackTrace ?? "").Split('\n')[0])}"); }
        }

        static void StopMicrophone()
        {
            foreach (var device in Microphone.devices) if (Microphone.IsRecording(device)) Microphone.End(device);
        }

        void WatchConnection()
        {
            if (lan == null) lan = FindAnyObjectByType<LanConnector>();
            if (lan != null && lan.Status != lastStatus) { Ev("connection", $"\"from\":{Q(lastStatus.ToString())},\"to\":{Q(lan.Status.ToString())}"); lastStatus = lan.Status; }
            int p = NetworkPlayer.All.Count;
            if (p != lastPlayers) { Ev("players", $"\"count\":{p}"); lastPlayers = p; }
        }

        // Count every RPC this instance receives, by method name (FishNet receive tables wrapped once per object).
        void WrapRpcs()
        {
            if (Time.realtimeSinceStartup < nextWrap) return;
            nextWrap = Time.realtimeSinceStartup + 1f;
            var round = NetworkRound.Instance;
            if (round != null) Wrap(round);
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null) continue;
                Wrap(p);
                var v = p.GetComponent<PlayerVoice>(); if (v != null) Wrap(v);
                var m = p.GetComponent<PlayerMovement>(); if (m != null) Wrap(m);
            }
        }

        // Wraps every delegate not wrapped yet (tables fill when the object initialises, possibly after it is first seen).
        void Wrap(NetworkBehaviour behaviour)
        {
            if (behaviour == null) return;
            foreach (var field in new[] { "_targetRpcDelegates", "_observersRpcDelegates" })
            {
                if (!(Get(behaviour, field) is Dictionary<uint, ClientRpcDelegate> table)) continue;
                foreach (var key in table.Keys.ToList())
                {
                    var original = table[key];
                    if (wrappers.Contains(original)) continue;
                    string name = original.Method.Name;
                    int cut = name.IndexOf("___", StringComparison.Ordinal);
                    string shortName = cut >= 0 ? name.Substring(cut + 3) : name;
                    int tail = shortName.LastIndexOf("___", StringComparison.Ordinal);
                    if (tail > 0) shortName = shortName.Substring(0, tail);
                    string owner = behaviour.GetType().Name;
                    try { if (behaviour.IsSpawned && behaviour.Owner != null) owner += "#" + behaviour.OwnerId; } catch { }
                    ClientRpcDelegate wrapper = (reader, channel) =>
                    {
                        rpcThisSecond[shortName] = rpcThisSecond.TryGetValue(shortName, out int n) ? n + 1 : 1;
                        if (!shortName.Contains("PlayVoice") && !shortName.Contains("MovementStep") && !shortName.Contains("SoundCanFrame") && !shortName.Contains("Sprint") && !shortName.Contains("Breath"))
                            Ev("rpc", $"\"name\":{Q(shortName)},\"on\":{Q(owner)}");
                        original(reader, channel);
                    };
                    wrappers.Add(wrapper);
                    table[key] = wrapper;
                    if (!wrapped.Contains(behaviour)) { wrapped.Add(behaviour); }
                }
            }
        }

        WalkingFootRings Rings() => NetworkPlayer.Local != null ? NetworkPlayer.Local.GetComponentInChildren<WalkingFootRings>() : null;

        void WatchRings()
        {
            var rings = Rings();
            if (rings == null || !(Get(rings, "rings") is IList list)) return;
            ringNow.Clear();
            foreach (var ring in list)
            {
                ringNow.Add(ring);
                if (seenRings.Contains(ring)) continue;
                var go = Get(ring, "Object") as GameObject;
                Ev("ring", $"\"sound\":{B(Bo(Get(ring, "Sound")))},\"self\":{B(Bo(Get(ring, "Self")))},\"seeker\":{B(Bo(Get(ring, "Seeker")))},\"r\":{F(Fo(Get(ring, "Radius")))},\"life\":{F(Fo(Get(ring, "Lifetime")))},\"at\":{(go != null ? V(go.transform.position) : "null")},\"me\":{V(NetworkPlayer.Local.transform.position)},\"wall\":{B(Bo(Get(ring, "HasWall")))}");
            }
            seenRings.Clear();
            foreach (var r in ringNow) seenRings.Add(r);
        }

        void WatchSounds()
        {
            var playback = FindAnyObjectByType<RoundAudioPlayback>();
            if (playback == null || !(Get(playback, "voices") is IList voices)) return;
            foreach (var voice in voices)
            {
                var source = Get(voice, "Source") as AudioSource;
                if (source == null || source.clip == null) continue;
                int stamp = source.timeSamples < 2048 && source.isPlaying ? Time.frameCount : -1;
                if (seenVoices.TryGetValue(voice, out int known) && (stamp < 0 || known >= Time.frameCount - 2)) continue;
                if (stamp < 0 && seenVoices.ContainsKey(voice)) continue;
                seenVoices[voice] = Time.frameCount;
                Ev("sound", $"\"clip\":{Q(source.clip.name)},\"at\":{V(source.transform.position)},\"muffled\":{B(Get(voice, "Muffled") is bool m && m)},\"loop\":{Get(voice, "LoopKey") ?? -1},\"vol\":{F(source.volume)},\"me\":{(NetworkPlayer.Local != null ? V(NetworkPlayer.Local.transform.position) : "null")}");
            }
        }

        // Received voice: samples added to each remote speaker's playback queue (320 per 20 ms packet).
        void WatchVoice()
        {
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null || p.IsOwner) continue;
                var voice = p.GetComponent<PlayerVoice>();
                if (voice == null || !(Get(voice, "audioQueue") is float[] queue)) continue;
                int write = (int)Get(voice, "audioWritePosition");
                if (!queueWrite.TryGetValue(p, out int previous)) { queueWrite[p] = write; continue; }
                int added = (write - previous + queue.Length) % queue.Length;
                queueWrite[p] = write;
                if (added <= 0) continue;
                int packets = Mathf.RoundToInt(added / (float)VoiceCodec.SamplesPerPacket);
                if (!packetsThisSecond.ContainsKey(p)) Ev("voiceStart", $"\"from\":{p.PlayerId}");
                packetsThisSecond[p] = (packetsThisSecond.TryGetValue(p, out int n) ? n : 0) + packets;
                var filter = Get(voice, "voiceFilter") as Behaviour;
                var source = Get(voice, "voiceSource") as AudioSource;
                bool blocked = filter != null && filter.enabled;
                if (!lastBlocked.TryGetValue(p, out bool was) || was != blocked)
                {
                    lastBlocked[p] = blocked;
                    Ev("voiceState", $"\"from\":{p.PlayerId},\"blocked\":{B(blocked)},\"maxDistance\":{(source != null ? F(source.maxDistance) : "null")},\"at\":{(source != null ? V(source.transform.position) : "null")}");
                }
            }
        }

        void WatchHints()
        {
            var t = T("Marco.Network.Vision.DirectionHints");
            if (t == null) return;
            int shown = (int)Get(t, "Shown");
            if (shown == lastShown) return;
            if (lastShown >= 0)
            {
                var view = NetworkPlayer.Local != null ? NetworkPlayer.Local.GetComponentInChildren<Camera>(true) : null;
                Ev("hint", $"\"shown\":{shown},\"extended\":{Get(t, "Extended")},\"octant\":{Get(t, "LastOctant")},\"viewYaw\":{(view != null ? F(view.transform.eulerAngles.y) : "null")},\"me\":{(NetworkPlayer.Local != null ? V(NetworkPlayer.Local.transform.position) : "null")}");
            }
            lastShown = shown;
        }

        void Frames()
        {
            frames.Add(Time.unscaledDeltaTime * 1000f);
            float now = Time.realtimeSinceStartup;
            if (now - secondStart < 1f) return;
            secondStart = now;
            var sorted = frames.OrderBy(x => x).ToList();
            float mean = sorted.Count > 0 ? sorted.Average() : 0, p99 = sorted.Count > 0 ? sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * .99f))] : 0;
            string rpc = string.Join(",", rpcThisSecond.Select(kv => Q(kv.Key) + ":" + kv.Value));
            string voice = string.Join(",", packetsThisSecond.Select(kv => Q(kv.Key.PlayerId.ToString()) + ":" + kv.Value));
            Ev("second", $"\"frames\":{sorted.Count},\"meanMs\":{F(mean)},\"p99Ms\":{F(p99)},\"maxMs\":{F(sorted.Count > 0 ? sorted[sorted.Count - 1] : 0)},\"rpc\":{{{rpc}}},\"voicePackets\":{{{voice}}},\"heard\":{heard}");
            frames.Clear(); rpcThisSecond.Clear(); packetsThisSecond.Clear();
            if (now - lastMemory >= 10f)
            {
                lastMemory = now;
                Ev("memory", $"\"gcMB\":{F(GC.GetTotalMemory(false) / 1048576f)},\"allocatedMB\":{F(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f)},\"workingSetMB\":{F(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576f)},\"errors\":{errors},\"warnings\":{warnings}");
            }
        }

        void Summary(string reason)
        {
            Ev("summary", $"\"reason\":{Q(reason)},\"errors\":{errors},\"warnings\":{warnings},\"warningKinds\":{{{string.Join(",", warnKinds.Select(kv => Q(kv.Key) + ":" + kv.Value))}}},\"heard\":{heard}");
        }

        void Shot(string name)
        {
            string file = Path.Combine(outDir, $"{scenario}_{Who}_{name}.png");
            ScreenCapture.CaptureScreenshot(file);
            Ev("shot", $"\"file\":{Q(file)}");
        }

        // ---------------------------------------------------------------- command channel (host → clients)
        int seq, lastSeq;
        static string CmdFile => Path.Combine(syncDir, "cmd.txt");
        void Cmd(string target, string cmd, string args = "")
        {
            seq++;
            File.AppendAllText(CmdFile, $"{seq}\t{target}\t{cmd}\t{args}\n");
            Ev("cmd", $"\"seq\":{seq},\"target\":{Q(target)},\"cmd\":{Q(cmd)},\"args\":{Q(args)}");
        }

        IEnumerator ClientCommands()
        {
            while (true)
            {
                if (File.Exists(CmdFile))
                {
                    string[] lines;
                    try { lines = File.ReadAllLines(CmdFile); } catch { lines = new string[0]; }
                    foreach (var line in lines)
                    {
                        var p = line.Split('\t');
                        if (p.Length < 4 || !int.TryParse(p[0], out int n) || n <= lastSeq) continue;
                        lastSeq = n;
                        if (p[1] != "*" && p[1] != hid.ToString()) continue;
                        Ev("exec", $"\"seq\":{n},\"cmd\":{Q(p[2])},\"args\":{Q(p[3])}");
                        StartCoroutine(Execute(p[2], p[3]));
                    }
                }
                yield return new WaitForSecondsRealtime(.05f);
            }
        }

        IEnumerator Execute(string cmd, string args)
        {
            var a = args.Split(' ');
            switch (cmd)
            {
                case "voice": yield return Speak(a[0], Fl(a, 1, -25), Fl(a, 2, 2)); break;
                case "pattern": yield return SpeakPattern(a[0], Fl(a, 1, 3)); break;
                case "sprint": yield return Sprint(Fl(a, 0, 10)); break;
                case "shot": Shot(a[0]); break;
                case "shotat":
                    {
                        long at = long.Parse(a[0]);
                        while (Ms < at) yield return null;
                        Shot(a[1]); DumpRingProps(a[1]);
                        break;
                    }
                case "props": DumpRingProps(a[0]); break;
                case "hint": RippleSwitch(a[0] == "on"); Ev("hintSwitch", $"\"on\":{B(a[0] == "on")}"); break;
                case "leave": lan?.Leave(); break;
                case "join": lan?.Join("127.0.0.1"); break;
                case "quit": Summary("quit command"); yield return new WaitForSecondsRealtime(.5f); Application.Quit(); break;
            }
        }

        static float Fl(string[] a, int i, float fallback) =>
            i < a.Length && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        // ---------------------------------------------------------------- synthetic voice (into PlayerVoice, like the mic would)
        // The microphone controller forwards a 20 ms frame (960 samples @ 48 kHz) once 3 frames in a row are above
        // −50 dBFS, graded −15 / −30 / −50 dBFS → Shout / Talk / Whisper. This mimics that, then calls the same method.
        System.Random noiseRandom = new System.Random(5);
        int consecutive;
        double phase;
        int framesSent;

        float[] Frame(string signal, float dbfs, double t)
        {
            var f = new float[VoiceCodec.InputSamplesPerPacket];
            float amp = Mathf.Pow(10f, dbfs / 20f);
            for (int i = 0; i < f.Length; i++)
            {
                double ts = t + i / 48000.0;
                float s = 0;
                switch (signal)
                {
                    case "voice":   // 200–1000 Hz partials, syllables 0.2 s on / 0.2 s off
                        {
                            bool on = ((int)(ts / .2)) % 2 == 0;
                            s = on ? (float)(Math.Sin(2 * Math.PI * 220 * ts) * .5 + Math.Sin(2 * Math.PI * 440 * ts) * .35 + Math.Sin(2 * Math.PI * 660 * ts) * .25 + Math.Sin(2 * Math.PI * 950 * ts) * .15) * 1.25f * amp : 0;
                            break;
                        }
                    case "steady":  // the same partials without syllable gaps
                        s = (float)(Math.Sin(2 * Math.PI * 220 * ts) * .5 + Math.Sin(2 * Math.PI * 440 * ts) * .35 + Math.Sin(2 * Math.PI * 660 * ts) * .25 + Math.Sin(2 * Math.PI * 950 * ts) * .15) * 1.25f * amp;
                        break;
                    case "keyboard": // 5 ms broadband clicks every 120 ms
                        s = (ts % .12) < .005 ? (float)(noiseRandom.NextDouble() * 2 - 1) * amp * 4f : 0;
                        break;
                    case "fan":     // 50–120 Hz hum
                        s = (float)(Math.Sin(2 * Math.PI * 60 * ts) * .6 + Math.Sin(2 * Math.PI * 90 * ts) * .5 + Math.Sin(2 * Math.PI * 115 * ts) * .4) * amp;
                        break;
                    case "white":
                        s = (float)(noiseRandom.NextDouble() * 2 - 1) * amp * 1.732f;
                        break;
                }
                f[i] = s;
            }
            return f;
        }

        static float Db(float[] f)
        {
            double sq = 0; foreach (var x in f) sq += x * x;
            return 20f * Mathf.Log10(Mathf.Max(Mathf.Sqrt((float)(sq / f.Length)), 1e-6f));
        }

        // Returns true when the frame was handed to PlayerVoice (the mic's 3-frame recognition passed).
        bool Inject(float[] frame, VoiceGrade? forced = null)
        {
            var voice = NetworkPlayer.Local != null ? NetworkPlayer.Local.GetComponent<PlayerVoice>() : null;
            if (voice == null) return false;
            float db = Db(frame);
            int level = db >= -15f ? 3 : db >= -30f ? 2 : db > -50f ? 1 : 0;
            consecutive = level == 0 ? 0 : consecutive + 1;
            if (level == 0 || consecutive < 3) return false;
            var grade = forced ?? (level == 3 ? VoiceGrade.Shout : level == 2 ? VoiceGrade.Talk : VoiceGrade.Whisper);
            Call(voice, "OnVoiceSamplesReady", frame, grade);
            framesSent++;
            return true;
        }

        IEnumerator Speak(string signal, float dbfs, float seconds)
        {
            Ev("speakStart", $"\"signal\":{Q(signal)},\"dbfs\":{F(dbfs)},\"seconds\":{F(seconds)}");
            framesSent = 0; consecutive = 0;
            float start = Time.realtimeSinceStartup; int done = 0, onsets = 0; bool wasOn = false;
            var gate = NetworkPlayer.Local != null ? Get(NetworkPlayer.Local.GetComponent<PlayerVoice>(), "activityGate") : null;
            int gateOpenFrames = 0;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                int due = (int)((Time.realtimeSinceStartup - start) * 50);
                while (done < due)
                {
                    double t = done * .02;
                    var frame = Frame(signal, dbfs, t);
                    bool on = Db(frame) > -50f;
                    if (on && !wasOn) { onsets++; Ev("onset", $"\"n\":{onsets},\"signal\":{Q(signal)}"); }
                    wasOn = on;
                    Inject(frame);
                    if (gate != null && Get(gate, "IsOpen") is bool open && open) gateOpenFrames++;
                    done++;
                }
                yield return null;
            }
            Ev("speakEnd", $"\"signal\":{Q(signal)},\"frames\":{done},\"handedToVoice\":{framesSent},\"gateOpenFrames\":{gateOpenFrames},\"onsets\":{onsets}");
        }

        // Grade changes for S9: "talk-shout-talk" (1 s each) or "flicker" (Talk↔Shout every 200 ms).
        IEnumerator SpeakPattern(string pattern, float seconds)
        {
            Ev("patternStart", $"\"pattern\":{Q(pattern)}");
            float start = Time.realtimeSinceStartup; int done = 0;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                int due = (int)((Time.realtimeSinceStartup - start) * 50);
                while (done < due)
                {
                    double t = done * .02;
                    float db = pattern == "flicker" ? (((int)(t / .2)) % 2 == 0 ? -25f : -10f) : (t < 1 ? -25f : t < 2 ? -10f : -25f);
                    Inject(Frame("steady", db, t));
                    done++;
                }
                yield return null;
            }
            Ev("patternEnd", $"\"pattern\":{Q(pattern)},\"frames\":{done}");
        }

        // Sprint-speed movement on a 3 m circle (7.5 m/s) around the current spot, through the owner's teleport.
        IEnumerator Sprint(float seconds)
        {
            var me = NetworkPlayer.Local;
            if (me == null) yield break;
            var movement = me.GetComponent<PlayerMovement>();
            var center = me.transform.position;
            float start = Time.realtimeSinceStartup, angle = 0;
            Ev("sprintStart", $"\"at\":{V(center)}");
            while (Time.realtimeSinceStartup - start < seconds)
            {
                angle += 7.5f / 3f * Time.deltaTime;
                movement.Teleport(center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 3f);
                yield return null;
            }
            movement.Teleport(center);
            Ev("sprintEnd");
        }

        void DumpRingProps(string tag)
        {
            var rings = Rings();
            if (rings == null) return;
            if (Get(rings, "rings") is IList list)
                foreach (var ring in list)
                {
                    var props = Get(ring, "Properties") as MaterialPropertyBlock;
                    if (props == null) continue;
                    Ev("ringProps", $"\"tag\":{Q(tag)},\"self\":{B(Bo(Get(ring, "Self")))},\"sound\":{B(Bo(Get(ring, "Sound")))},\"alpha\":{F(props.GetColor("_BaseColor").a)},\"radius\":{F(props.GetFloat("_Radius"))},\"core\":{F(props.GetFloat("_Core"))},\"trail\":{F(props.GetFloat("_Trail"))},\"maxR\":{F(Fo(Get(ring, "Radius")))},\"age\":{F(Time.time - Fo(Get(ring, "Started"), Time.time))}");
                }
            if (Get(rings, "afterglows") is IList glows)
                foreach (var glow in glows)
                {
                    var props = Get(glow, "Properties") as MaterialPropertyBlock;
                    var go = Get(glow, "Object") as GameObject;
                    if (props == null || go == null || !go.activeSelf) continue;
                    Ev("afterglowProps", $"\"tag\":{Q(tag)},\"alpha\":{F(props.GetColor("_BaseColor").a)},\"age\":{F(Time.time - Fo(Get(glow, "Started"), Time.time))}");
                }
        }

        // ---------------------------------------------------------------- scenario entry
        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1f);
            while ((lan = FindAnyObjectByType<LanConnector>()) == null) yield return null;
            Ev("scene", $"\"scenes\":{Q(string.Join(";", Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path)))},\"intro\":{Q(IntroState())}");
            if (scenario == "lobby") yield return LobbyIntroPhase();
            if (scenario == "relay") { yield return IsHost ? RelayHost() : RelayClient(); yield break; }
            if (!IsHost) { yield return ClientMain(); yield break; }
            yield return HostMain();
        }

        static string IntroState()
        {
            var intro = FindAnyObjectByType(T("Marco.Network.Lobby.LobbyIntro") ?? typeof(MonoBehaviour)) as MonoBehaviour;
            if (intro == null || intro.GetType().Name != "LobbyIntro") return "absent";
            return $"playing={Get(intro, "IsPlaying")} phase={Get(intro, "phase")}";
        }

        // ---------------------------------------------------------------- client
        IEnumerator ClientMain()
        {
            StartCoroutine(ClientCommands());
            yield return Connect();
            while (true)
            {
                // Host shutting down ends the client; an unplanned drop is logged by WatchConnection.
                if (lan.Status != LanConnectionStatus.Connected && lastPlayers == 0 && File.Exists(Path.Combine(syncDir, "done")))
                {
                    Summary("host done");
                    yield return new WaitForSecondsRealtime(.5f);
                    Application.Quit();
                    yield break;
                }
                if (NetworkPlayer.Local != null) WriteReady();
                yield return new WaitForSecondsRealtime(.5f);
            }
        }

        int readyFor = -1;
        void WriteReady()
        {
            if (NetworkPlayer.Local.PlayerId == readyFor) return;
            readyFor = NetworkPlayer.Local.PlayerId;
            File.WriteAllText(Path.Combine(syncDir, $"ready_{hid}.txt"), readyFor.ToString());
            Ev("ready", $"\"playerId\":{readyFor}");
        }

        IEnumerator Connect()
        {
            float deadline = Time.realtimeSinceStartup + 120;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (lan.Status == LanConnectionStatus.Offline || lan.Status == LanConnectionStatus.Failed) Ev("joinCall", $"\"ok\":{B(lan.Join("127.0.0.1"))}");
                float wait = Time.realtimeSinceStartup + 6;
                while (Time.realtimeSinceStartup < wait && lan.Status != LanConnectionStatus.Connected) yield return null;
                if (lan.Status == LanConnectionStatus.Connected)
                {
                    float spawn = Time.realtimeSinceStartup + 15;
                    while (NetworkPlayer.Local == null && Time.realtimeSinceStartup < spawn) yield return null;
                    if (NetworkPlayer.Local != null) { WriteReady(); yield break; }
                }
                yield return new WaitForSecondsRealtime(2);
            }
            Ev("connectFailed");
        }

        // ---------------------------------------------------------------- host helpers
        NetworkRound Round => NetworkRound.Instance;
        NetworkPlayer Host => NetworkPlayer.Local;

        NetworkPlayer P(int id)
        {
            if (id == 0) return Host;
            var file = Path.Combine(syncDir, $"ready_{id}.txt");
            if (!File.Exists(file) || !int.TryParse(File.ReadAllText(file).Trim(), out int playerId)) return null;
            return NetworkPlayer.TryGet(playerId, out var player) ? player : null;
        }

        void Step(string name, string body = null) => Ev("step", $"\"name\":{Q(name)}{(body != null ? "," + body : "")}");

        void Tp(int id, Vector3 position, Vector3? lookAt = null)
        {
            var player = P(id);
            if (player == null) return;
            var rotation = lookAt.HasValue ? Quaternion.LookRotation(Vector3.ProjectOnPlane(lookAt.Value - position, Vector3.up).normalized) : player.transform.rotation;
            if (id == 0) { player.GetComponent<PlayerMovement>().TeleportToSpawn(position, rotation); Physics.SyncTransforms(); }
            else player.TeleportOwner(position, rotation);
            Ev("tp", $"\"id\":{id},\"to\":{V(position)}");
        }

        static bool Floor(Vector3 near, out Vector3 spot)
        {
            spot = default;
            if (!Physics.Raycast(near + Vector3.up * 2.5f, Vector3.down, out var floor, 4f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (floor.normal.y < .7f || Physics.CheckSphere(floor.point + Vector3.up * 1f, .35f, ~0, QueryTriggerInteraction.Ignore)) return false;
            spot = floor.point + Vector3.up * 1.05f;
            return true;
        }

        // A floor spot `distance` from `center` with a clear eye-height line to it (no wall between).
        static bool OpenSpot(Vector3 center, float distance, out Vector3 spot, float startAngle = 0)
        {
            for (int i = 0; i < 24; i++)
            {
                float angle = startAngle + i * Mathf.PI / 12f;
                if (!Floor(center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance, out spot)) continue;
                if (Mathf.Abs(Vector2.Distance(new Vector2(spot.x, spot.z), new Vector2(center.x, center.z)) - distance) > .3f) continue;
                if (Physics.Linecast(center + Vector3.up * .6f, spot + Vector3.up * .6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                return true;
            }
            spot = default;
            return false;
        }

        // Like OpenSpot, but of every open spot at that distance the one closest to the centre's height. The server judges
        // hearing at eye height, so a listener put on top of a machine 3 m above a valve is out of a 6 m radius at "5 m".
        static bool LevelSpot(Vector3 center, float distance, out Vector3 spot)
        {
            spot = default;
            float best = float.MaxValue;
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI / 12f;
                if (!Floor(center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance, out var s)) continue;
                if (Mathf.Abs(Vector2.Distance(new Vector2(s.x, s.z), new Vector2(center.x, center.z)) - distance) > .3f) continue;
                if (Physics.Linecast(center + Vector3.up * .6f, s + Vector3.up * .6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                float dy = Mathf.Abs(s.y - center.y);
                if (dy < best) { best = dy; spot = s; }
            }
            return best < float.MaxValue;
        }

        // A centre with open spots in every distance asked (along one bearing), for range tests.
        static bool OpenLine(float[] distances, out Vector3 center, out Vector3[] spots)
        {
            spots = new Vector3[distances.Length];
            var candidates = new List<Vector3>();
            foreach (var exit in ExitView.All) if (exit != null) candidates.Add(exit.transform.position);
            for (float x = 4; x < 62; x += 6) for (float z = 4; z < 60; z += 6) candidates.Add(new Vector3(x, 1.5f, z));
            foreach (var c in candidates)
            {
                if (!Floor(c, out center)) continue;
                for (int a = 0; a < 16; a++)
                {
                    float angle = a * Mathf.PI / 8f;
                    var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    bool ok = true;
                    for (int i = 0; i < distances.Length && ok; i++)
                    {
                        ok = Floor(center + dir * distances[i], out spots[i]) && Mathf.Abs(spots[i].y - center.y) < .3f &&
                             !Physics.Linecast(center + Vector3.up * .6f, spots[i] + Vector3.up * .6f, ~0, QueryTriggerInteraction.Ignore);
                    }
                    if (ok) return true;
                }
            }
            center = default;
            return false;
        }

        (bool heard, bool blocked, float perceived) Judge(Vector3 source, NetworkPlayer listener, float radius)
        {
            var args = new object[] { source, listener, radius, null, null, null, null };
            bool heard = (bool)Call(Round, "TryHearPoolVoice", args);
            return (heard, (bool)args[5], (float)args[6]);
        }

        // A spot where `listenerId` hears `source` through a wall or the water surface (the host player is the probe).
        IEnumerator HiddenSpot(Vector3 source, float radius, int listenerId, Action<Vector3?> found)
        {
            var host = Host; var listener = P(listenerId);
            var movement = host.GetComponent<PlayerMovement>();
            var home = host.transform.position;
            float listenerScale = listener != null && listener.Role == PlayerRole.Seeker ? 1.2f : 1f, hostScale = host.Role == PlayerRole.Seeker ? 1.2f : 1f;
            Vector3? result = null;
            for (float d = 1.5f; d < radius * .5f * listenerScale - .4f && result == null; d += .75f)
                for (int i = 0; i < 16 && result == null; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    if (!Floor(source + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * d, out var at)) continue;
                    movement.Teleport(at);
                    Physics.SyncTransforms();
                    var j = Judge(source, host, radius * listenerScale / hostScale);
                    if (j.heard && j.blocked) result = at;
                }
            movement.Teleport(home);
            Physics.SyncTransforms();
            found(result);
            yield return null;
        }

        // Near a doorway: a speaker spot and a listener spot with the wall between (listener within the walled range).
        IEnumerator WalledPair(float radius, int listenerId, Action<Vector3?, Vector3?> found)
        {
            Vector3? speaker = null, listener = null;
            var parts = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude).Where(p => p != null && p.GetType().Name == "PoolV12Part" &&
                (Get(p, "category") as string) is string c && (c == "Door" || c == "ExternalDoor") && !(Get(p, "floor") is int f && f >= 2)).ToList();
            foreach (var door in parts)
            {
                if (speaker != null) break;
                if (!(Get(door, "designPosition") is Vector3 center)) continue;
                foreach (var offset in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    if (!Floor(center + offset * 2f, out var spot)) continue;
                    Vector3? hidden = null;
                    yield return HiddenSpot(spot + Vector3.up * .6f, radius, listenerId, v => hidden = v);
                    if (hidden == null) continue;
                    speaker = spot; listener = hidden;
                    break;
                }
            }
            found(speaker, listener);
        }

        static int Octant(Vector3 source, Vector3 listener)
        {
            float yaw = Mathf.Atan2(source.x - listener.x, source.z - listener.z) * Mathf.Rad2Deg;
            return ((int)Mathf.Round(yaw / 45f) % 8 + 8) % 8;
        }

        bool Want(string step) => steps.Count == 0 || steps.Contains(step) || steps.Contains("all");

        IEnumerator WaitPlayers(int count, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool all = NetworkPlayer.All.Count >= count;
                for (int i = 1; i < count && all; i++) all = P(i) != null;
                if (all) { Step("playersReady", $"\"count\":{NetworkPlayer.All.Count}"); yield break; }
                yield return null;
            }
            Step("playersTimeout", $"\"count\":{NetworkPlayer.All.Count}");
        }

        IEnumerator StartRound(int number)
        {
            if (number == 1) Round.StartRound(); else Round.RestartRound();
            yield return new WaitForSecondsRealtime(.6f);
            // Host plays the Seeker so the clients are Runners (role swap is the existing debug cheat).
            if (Host.Role != PlayerRole.Seeker && Has(Round, "CheatToggleHostRole", 0)) { Call(Round, "CheatToggleHostRole"); yield return new WaitForSecondsRealtime(.4f); }
            string roles = string.Join(" ", NetworkPlayer.All.Select(p => $"P{p.PlayerId}:{p.Role}/{p.Status}"));
            Step("round", $"\"n\":{number},\"mode\":{Q(Round.Mode.ToString())},\"roles\":{Q(roles)}");
            float released = Time.realtimeSinceStartup + 15;
            while (Time.realtimeSinceStartup < released && Get(Round, "StartPhase")?.ToString() != "Released") yield return null;
            Step("released", $"\"phase\":{Q(Get(Round, "StartPhase")?.ToString())}");
        }

        IEnumerator Finish()
        {
            Summary("host done");
            File.WriteAllText(Path.Combine(syncDir, "done"), "1");
            Cmd("*", "quit");
            yield return new WaitForSecondsRealtime(3);
            lan.Leave();
            yield return new WaitForSecondsRealtime(2);
            Application.Quit();
        }

        // ---------------------------------------------------------------- host scenarios
        IEnumerator HostMain()
        {
            File.Delete(CmdFile);
            foreach (var f in Directory.GetFiles(syncDir, "ready_*.txt")) File.Delete(f);
            if (File.Exists(Path.Combine(syncDir, "done"))) File.Delete(Path.Combine(syncDir, "done"));
            lan.Host();
            Step("hostCall");
            yield return WaitPlayers(expectedPlayers, 240);
            yield return new WaitForSecondsRealtime(2);
            switch (scenario)
            {
                case "main": yield return MainScenario(); break;
                case "load": yield return LoadScenario(); break;
                case "pair": yield return PairScenario(); break;
                case "compat": yield return CompatScenario(); break;
                case "gate": yield return GateScenario(); break;
                case "lobby": yield return LobbyScenario(); break;
                case "visual": yield return VisualScenario(); break;
                case "soak": yield return SoakScenario(); break;
            }
            yield return Finish();
        }

        // 4-B · 4-C · 4-D.1 with host (Seeker) + 3 Runners.
        IEnumerator MainScenario()
        {
            for (int r = 1; r <= rounds; r++)
            {
                yield return StartRound(r);
                if (r == 1)
                {
                    if (Want("voice")) yield return VoiceTests();
                    if (Want("S9")) yield return S9();
                    if (Want("S1")) yield return S1S11();
                    if (Want("S2")) yield return S2();
                    if (Want("S3")) yield return S3();
                    if (Want("hint")) yield return HintTests();
                    if (Want("S4")) yield return S4();
                    if (Want("S5")) yield return S5();
                    if (Want("S8")) yield return S8();
                }
                else if (r == 2)
                {
                    if (Want("S6")) yield return S6S7();
                    if (Want("S2")) yield return S2();
                }
                else if (Want("S2")) yield return S2();
                Step("roundEnd", $"\"n\":{r},\"result\":{Q(Round.Result.ToString())},\"players\":{NetworkPlayer.All.Count}");
            }
        }

        IEnumerator VoiceTests()
        {
            // Range: speaker c1 at the centre; c2 inside, c3 outside, host (Seeker × 1.2) just outside the Runner range.
            var grades = new (string name, float db, float range)[] { ("Whisper", -40f, 4f), ("Talk", -25f, 9f), ("Shout", -10f, 22f) };
            foreach (var g in grades)
            {
                var d = new[] { g.range - .6f, g.range + .6f, g.range * 1.2f - .5f };
                if (!OpenLine(d, out var center, out var spots)) { Step("voiceRange", $"\"grade\":{Q(g.name)},\"result\":\"no open line for {F(d[2])} m\""); continue; }
                Tp(1, center, spots[0]); Tp(2, spots[0], center); Tp(3, spots[1], center); Tp(0, spots[2], center);
                yield return new WaitForSecondsRealtime(2.2f);
                Step("voiceRange", $"\"grade\":{Q(g.name)},\"range\":{F(g.range)},\"speaker\":{P(1).PlayerId},\"inRunner\":{P(2).PlayerId},\"outRunner\":{P(3).PlayerId},\"seekerHost\":{Host.PlayerId},\"d\":[{F(Vector3.Distance(P(1).transform.position, P(2).transform.position))},{F(Vector3.Distance(P(1).transform.position, P(3).transform.position))},{F(Vector3.Distance(P(1).transform.position, Host.transform.position))}]");
                Cmd("1", "voice", $"steady {F(g.db)} 3");
                yield return new WaitForSecondsRealtime(4f);
            }
            // One wall: Talk 9 m → 4.5 m, muffled; c2 behind the wall inside 4.5 m.
            Vector3? speakerSpot = null, inside = null;
            yield return WalledPair(9f, 2, (a, b) => { speakerSpot = a; inside = b; });
            if (inside != null)
            {
                var speaker = speakerSpot.Value + Vector3.up * .6f;
                Tp(1, speakerSpot.Value);
                Tp(2, inside.Value, speaker);
                yield return new WaitForSecondsRealtime(2.2f);
                var j = Judge(speaker, P(2), 9f);
                Step("voiceWall", $"\"listener\":{P(2).PlayerId},\"heard\":{B(j.heard)},\"blocked\":{B(j.blocked)},\"perceived\":{F(j.perceived)},\"d\":{F(Vector3.Distance(speaker, P(2).transform.position))}");
                Cmd("1", "voice", "steady -25 3");
                yield return new WaitForSecondsRealtime(4f);
            }
            else Step("voiceWall", "\"result\":\"no walled spot\"");
            // Both directions: the host talks to c1 at 3 m.
            if (OpenSpot(Host.transform.position, 3f, out var near)) { Tp(1, near, Host.transform.position); yield return new WaitForSecondsRealtime(2.2f); }
            Step("voiceHostToClient", $"\"listener\":{P(1).PlayerId}");
            yield return Speak("voice", -25f, 4f);
            yield return new WaitForSecondsRealtime(1.5f);
        }

        IEnumerator S9()
        {
            if (OpenSpot(P(1).transform.position, 3f, out var near)) Tp(2, near, P(1).transform.position);
            yield return new WaitForSecondsRealtime(2.2f);
            Step("S9", $"\"pattern\":\"talk-shout-talk\",\"speaker\":{P(1).PlayerId},\"listener\":{P(2).PlayerId}");
            Cmd("1", "pattern", "talk-shout-talk 3");
            yield return new WaitForSecondsRealtime(4f);
            Step("S9", $"\"pattern\":\"flicker\",\"speaker\":{P(1).PlayerId},\"listener\":{P(2).PlayerId}");
            Cmd("1", "pattern", "flicker 6");
            yield return new WaitForSecondsRealtime(7f);
        }

        // S1 breath: c2 and c3 stand still 2 m apart, the host 4 m from c2; 82 s → breaths at 20/35/50/65/80 s.
        // S11: c1 is held by the treatment lock for the same time: its breath timer must not run.
        IEnumerator S1S11()
        {
            if (!OpenLine(new[] { 2f, 4f, 9f }, out var center, out var spots)) { Step("S1", "\"result\":\"no open line\""); yield break; }
            Tp(2, center, spots[0]); Tp(3, spots[0], center); Tp(0, spots[1], center);
            Vector3 lockSpot = OpenSpot(spots[2], 3f, out var s) ? s : spots[2];
            Tp(1, lockSpot);
            yield return new WaitForSecondsRealtime(1.5f);
            P(1).SetTreatmentLocked(true);
            Step("S1", $"\"breather\":{P(2).PlayerId},\"near\":{P(3).PlayerId},\"hostAt\":4,\"locked\":{P(1).PlayerId}");
            float start = Time.realtimeSinceStartup;
            int lastCount = -1;
            while (Time.realtimeSinceStartup - start < 84f)
            {
                var motion = Get(Round, "soundMotion") as IDictionary;
                if (motion != null)
                {
                    var m2 = motion.Contains(P(2).PlayerId) ? motion[P(2).PlayerId] : null;
                    var m1 = motion.Contains(P(1).PlayerId) ? motion[P(1).PlayerId] : null;
                    int count = m2 != null && Get(m2, "BreathCount") is int c ? c : -1;
                    if (count != lastCount)
                    {
                        lastCount = count;
                        var radius = T("Marco.Network.Round.PulseSpec") != null && count > 0 ? Call(T("Marco.Network.Round.PulseSpec"), "BreathRadius", count - 1) : null;
                        Step("S1breath", $"\"count\":{count},\"radius\":{(radius != null ? F((float)radius) : "null")},\"still\":{F(m2 != null ? (float)Get(m2, "StillSeconds") : -1)},\"lockedStill\":{F(m1 != null ? (float)Get(m1, "StillSeconds") : -1)}");
                    }
                }
                yield return new WaitForSecondsRealtime(.25f);
            }
            var lockedMotion = (Get(Round, "soundMotion") as IDictionary)?[P(1).PlayerId];
            Step("S11", $"\"lockedStill\":{F(lockedMotion != null ? (float)Get(lockedMotion, "StillSeconds") : -1)},\"lockedBreaths\":{(lockedMotion != null ? Get(lockedMotion, "BreathCount") ?? "null" : "null")}");
            P(1).SetTreatmentLocked(false);
        }

        // S2: every active valve once; c2 at 5 m (inside 6 and 12), c3 at 8 m (outside A's 6 m, inside 12 m).
        IEnumerator S2()
        {
            var board = Get(Round, "board");
            if (!(Get(board, "Valves") is IDictionary valves)) yield break;
            foreach (var key in valves.Keys.Cast<object>().ToList())
            {
                var id = (ValveId)key;
                if (!ValveView.TryGet(id, out var view)) continue;
                var at = view.transform.position;
                bool underwater = id == ValveId.B || id == ValveId.E;
                if (!LevelSpot(at, 5f, out var five) || !LevelSpot(at, 8f, out var eight)) { Step("S2", $"\"valve\":{Q(id.ToString())},\"result\":\"no open spots\""); continue; }
                Tp(2, five, at); Tp(3, eight, at);
                yield return new WaitForSecondsRealtime(2.2f);
                float expected = Has(Round, "ValveSoundRadius", 1) ? (float)Call(Round, "ValveSoundRadius", id) : 12f;
                var j2 = Judge(at, P(2), expected); var j3 = Judge(at, P(3), expected);
                Step("S2", $"\"valve\":{Q(id.ToString())},\"underwater\":{B(underwater)},\"at\":{V(at)},\"expected\":{F(expected)},\"c2\":{F(Vector3.Distance(at, P(2).transform.position))},\"c3\":{F(Vector3.Distance(at, P(3).transform.position))},\"j2\":[{B(j2.heard)},{B(j2.blocked)},{F(j2.perceived)}],\"j3\":[{B(j3.heard)},{B(j3.blocked)},{F(j3.perceived)}]");
                Call(Round, "ValveAudio", id, ValveSoundEvent.RotatingStarted);
                yield return new WaitForSecondsRealtime(3f);
                Call(Round, "ValveAudio", id, ValveSoundEvent.RotatingStopped);
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        // S3: drowning at c1; c2 behind a wall (no ring), c3 in the open (ring).
        IEnumerator S3()
        {
            var source = P(1).transform.position;
            Vector3? hidden = null;
            yield return HiddenSpot(source, 22f, 2, v => hidden = v);
            if (hidden != null) Tp(2, hidden.Value, source);
            if (OpenSpot(source, 6f, out var open)) Tp(3, open, source);
            yield return new WaitForSecondsRealtime(2.2f);
            var j2 = Judge(source, P(2), 22f); var j3 = Judge(source, P(3), 22f);
            Step("S3", $"\"source\":{V(source)},\"walled\":{P(2).PlayerId},\"walledJudge\":[{B(j2.heard)},{B(j2.blocked)},{F(j2.perceived)}],\"open\":{P(3).PlayerId},\"openJudge\":[{B(j3.heard)},{B(j3.blocked)},{F(j3.perceived)}],\"expectedOctantWalled\":{Octant(source, P(2).transform.position)}");
            Round.EmitDrowningSound(P(1), source.y - .1f);
            yield return new WaitForSecondsRealtime(3f);
        }

        // Direction hints (client c2 switched on): hidden voice, hidden valve, hidden footsteps (not ring display), open voice.
        IEnumerator HintTests()
        {
            Cmd("2", "hint", "on");
            yield return new WaitForSecondsRealtime(.5f);
            Vector3? speakerSpot = null, hidden = null;
            yield return WalledPair(9f, 2, (a, b) => { speakerSpot = a; hidden = b; });
            if (hidden != null)
            {
                var speaker = speakerSpot.Value + Vector3.up * .6f;
                Tp(1, speakerSpot.Value);
                Tp(2, hidden.Value, speaker);
                yield return new WaitForSecondsRealtime(2.2f);
                Step("hintVoice", $"\"expectedOctant\":{Octant(speaker, P(2).transform.position)},\"listener\":{P(2).PlayerId}");
                Cmd("1", "voice", "voice -25 3");
                Cmd("2", "shotat", $"{Ms + 1500} hintVoice");
                yield return new WaitForSecondsRealtime(4f);
                Step("hintFootsteps", "\"expected\":\"no hint (movement is not ring display — §12.6 option)\"");
                Cmd("1", "sprint", "4");
                yield return new WaitForSecondsRealtime(5f);
            }
            else Step("hintVoice", "\"result\":\"no walled spot\"");
            var board = Get(Round, "board");
            if (Get(board, "Valves") is IDictionary valves)
                foreach (var key in valves.Keys.Cast<object>().ToList())
                {
                    var id = (ValveId)key;
                    if (!ValveView.TryGet(id, out var view)) continue;
                    float radius = Has(Round, "ValveSoundRadius", 1) ? (float)Call(Round, "ValveSoundRadius", id) : 12f;
                    Vector3? spot = null;
                    yield return HiddenSpot(view.transform.position, radius, 2, v => spot = v);
                    if (spot == null) continue;
                    Tp(2, spot.Value, view.transform.position);
                    yield return new WaitForSecondsRealtime(2.2f);
                    Step("hintValve", $"\"valve\":{Q(id.ToString())},\"expectedOctant\":{Octant(view.transform.position, P(2).transform.position)}");
                    Call(Round, "ValveAudio", id, ValveSoundEvent.RotatingStarted);
                    Cmd("2", "shotat", $"{Ms + 800} hintValve");
                    yield return new WaitForSecondsRealtime(3f);
                    Call(Round, "ValveAudio", id, ValveSoundEvent.RotatingStopped);
                    break;
                }
            if (OpenSpot(P(1).transform.position, 3f, out var open))
            {
                Tp(2, open, P(1).transform.position);
                yield return new WaitForSecondsRealtime(2.2f);
                Step("hintOpenVoice", "\"expected\":\"no hint (ring visible)\"");
                Cmd("1", "voice", "voice -25 3");
                yield return new WaitForSecondsRealtime(4f);
            }
            Cmd("2", "hint", hintOn ? "on" : "off");
        }

        // S4: first hit wounds (no pulse), the second turns the Runner into an Echo (22 m pulse once).
        IEnumerator S4()
        {
            var target = P(3);
            if (OpenSpot(target.transform.position, 6f, out var watch)) Tp(2, watch, target.transform.position);
            yield return new WaitForSecondsRealtime(2.2f);
            Step("S4hit1", $"\"target\":{target.PlayerId},\"watcher\":{P(2).PlayerId}");
            var first = Call(Round, "Hit", target);
            Step("S4hit1Result", $"\"result\":{Q(first?.ToString())}");
            yield return new WaitForSecondsRealtime(3f);
            Step("S4hit2", $"\"target\":{target.PlayerId}");
            var second = Call(Round, "Hit", target);
            Step("S4hit2Result", $"\"result\":{Q(second?.ToString())},\"at\":{V(target.transform.position)}");
            yield return new WaitForSecondsRealtime(4f);
        }

        // S5: gate opening (master-key debug cheat) — c1 near exit 1, c2 near exit 2, host far.
        IEnumerator S5()
        {
            var exits = ExitView.All.Where(e => e != null).Select(e => e.transform.position).ToList();
            if (exits.Count >= 2)
            {
                if (OpenSpot(exits[0], 5f, out var e1)) Tp(1, e1, exits[0]);
                if (OpenSpot(exits[1], 5f, out var e2)) Tp(2, e2, exits[1]);
            }
            yield return new WaitForSecondsRealtime(2.2f);
            Step("S5", $"\"exits\":[{string.Join(",", exits.Select(V))}],\"pulled\":\"CheatOpenActiveValves\"");
            Call(Round, "CheatOpenActiveValves");
            yield return new WaitForSecondsRealtime(4f);
        }

        // S8: fast-forward to 0:31 (debug cheat), then 30 s of exit pulses: c1 open near exit 1, c2 walled near an exit, c3 far.
        IEnumerator S8()
        {
            var exits = ExitView.All.Where(e => e != null).Select(e => e.transform.position).ToList();
            if (exits.Count == 0) yield break;
            if (OpenSpot(exits[0], 5f, out var e1)) Tp(1, e1, exits[0]);
            Vector3? walled = null;
            yield return HiddenSpot(exits[0], 22f, 2, v => walled = v);
            if (walled != null) Tp(2, walled.Value, exits[0]);
            var far = exits.Count > 1 ? (exits[0] + exits[1]) * .5f : exits[0] + Vector3.right * 30;
            if (Floor(far, out var farSpot)) Tp(3, farSpot);
            yield return new WaitForSecondsRealtime(2.2f);
            float remaining = Round.RemainingSeconds;
            Step("S8", $"\"remainingBefore\":{F(remaining)},\"open\":{P(1).PlayerId},\"walled\":{P(2).PlayerId},\"far\":{P(3).PlayerId},\"farDistances\":[{string.Join(",", exits.Select(e => F(Vector3.Distance(e, P(3).transform.position))))}],\"pulled\":\"CheatFastForward\"");
            if (remaining > 31) Call(Round, "CheatFastForward", remaining - 31f);
            yield return new WaitForSecondsRealtime(34f);
            Step("S8end", $"\"remaining\":{F(Round.RemainingSeconds)},\"result\":{Q(Round.Result.ToString())}");
        }

        // S6 / S7 in a fresh round: last-survivor entry (debug cheat tags down to one Runner), then drain work at the active hatch.
        IEnumerator S6S7()
        {
            // Listeners above both hatches first (the active one is chosen on entry): c1 → First, c2 → Second.
            int listener = 1;
            foreach (var hid in new[] { DrainHatchId.First, DrainHatchId.Second })
            {
                if (DrainHatchView.TryGet(hid, out var hv) && OpenSpot(new Vector3(hv.transform.position.x, hv.transform.position.y + 3.5f, hv.transform.position.z), 4f, out var above))
                {
                    Tp(listener, above, hv.transform.position);
                    Step("S6listener", $"\"hatch\":{Q(hid.ToString())},\"listener\":{P(listener).PlayerId},\"at\":{V(above)}");
                }
                listener++;
            }
            yield return new WaitForSecondsRealtime(2.2f);
            Call(Round, "CheatEnterLastSurvivorPhase");
            yield return new WaitForSecondsRealtime(1.5f);
            var phase = Get(Get(Round, "round"), "LastSurvivorPhase");
            var hatch = Get(phase, "ActiveDrainHatchId");
            NetworkPlayer survivor = NetworkPlayer.All.FirstOrDefault(p => p.Role == PlayerRole.Runner && p.Status == PlayerStatus.Alive);
            Step("S6", $"\"entered\":{Q(Get(phase, "HasEntered")?.ToString())},\"hatch\":{Q(hatch?.ToString())},\"survivor\":{(survivor != null ? survivor.PlayerId : -1)},\"pulled\":\"CheatEnterLastSurvivorPhase\"");
            if (hatch is DrainHatchId id && DrainHatchView.TryGet(id, out var view) && survivor != null)
            {
                var at = view.transform.position;
                int listenerId = id == DrainHatchId.First ? 1 : 2;
                if (P(listenerId) == survivor) listenerId = listenerId == 1 ? 2 : 1;
                if (listenerId > 0)
                {
                    var j = Judge(at, P(listenerId), 12f);
                    Step("S7", $"\"listener\":{P(listenerId).PlayerId},\"judge12\":[{B(j.heard)},{B(j.blocked)},{F(j.perceived)}],\"worker\":{survivor.PlayerId},\"pulled\":\"drains.TryBeginWork\"");
                }
                var drains = Get(Round, "drains");
                bool began = drains != null && (bool)Call(drains, "TryBeginWork", survivor.PlayerId, id);
                Step("S7work", $"\"began\":{B(began)}");
                yield return new WaitForSecondsRealtime(8f);
                if (began) Call(drains, "EndWork", survivor.PlayerId, id);
                Step("S7end");
            }
            yield return new WaitForSecondsRealtime(1f);
        }

        // 4-G.1: everyone shouts and sprints for 60 s.
        IEnumerator LoadScenario()
        {
            yield return StartRound(1);
            Vector3 center = Host.transform.position;
            for (int i = 1; i < expectedPlayers; i++) if (OpenSpot(center, 3f + i, out var s, i)) Tp(i, s, center);
            yield return new WaitForSecondsRealtime(2.5f);
            Step("load", "\"seconds\":60");
            Cmd("*", "voice", "voice -10 60");
            Cmd("*", "sprint", "60");
            StartCoroutine(Speak("voice", -10f, 60f));
            yield return Sprint(60f);
            Step("loadEnd");
            yield return new WaitForSecondsRealtime(2f);
        }

        // 4-A.4: the orchestrator kills the client process when it sees "killnow", then starts it again.
        IEnumerator PairScenario()
        {
            yield return StartRound(1);
            Cmd("1", "voice", "voice -25 3");
            yield return new WaitForSecondsRealtime(5f);
            Step("kill");
            File.WriteAllText(Path.Combine(syncDir, "killnow"), "1");
            float deadline = Time.realtimeSinceStartup + 120;
            while (NetworkPlayer.All.Count >= 2 && Time.realtimeSinceStartup < deadline) yield return null;
            Step("dropped", $"\"players\":{NetworkPlayer.All.Count},\"mode\":{Q(Round.Mode.ToString())}");
            File.Delete(Path.Combine(syncDir, "ready_1.txt"));
            while ((NetworkPlayer.All.Count < 2 || P(1) == null) && Time.realtimeSinceStartup < deadline) yield return null;
            Step("rejoined", $"\"players\":{NetworkPlayer.All.Count},\"mode\":{Q(Round.Mode.ToString())},\"roles\":{Q(string.Join(" ", NetworkPlayer.All.Select(p => $"P{p.PlayerId}:{p.Role}/{p.Status}")))}");
            yield return new WaitForSecondsRealtime(2f);
            for (int r = 2; r <= 3; r++)
            {
                yield return StartRound(r);
                Cmd("1", "voice", "voice -25 2");
                yield return Speak("voice", -25f, 2f);
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        // 4-A.6: mixed builds — connect, round, voice both ways, a valve pulse near the client.
        IEnumerator CompatScenario()
        {
            yield return StartRound(1);
            if (OpenSpot(Host.transform.position, 3f, out var near)) Tp(1, near, Host.transform.position);
            yield return new WaitForSecondsRealtime(2.2f);
            Step("compatVoice");
            Cmd("1", "voice", "voice -25 3");
            yield return new WaitForSecondsRealtime(3.5f);
            yield return Speak("voice", -25f, 3f);
            if (Get(Get(Round, "board"), "Valves") is IDictionary valves)
                foreach (var key in valves.Keys.Cast<object>().ToList())
                {
                    var id = (ValveId)key;
                    if (!ValveView.TryGet(id, out var view) || !LevelSpot(view.transform.position, 4f, out var spot)) continue;
                    Tp(1, spot, view.transform.position);
                    yield return new WaitForSecondsRealtime(2.2f);
                    Step("compatValve", $"\"valve\":{Q(id.ToString())}");
                    Call(Round, "ValveAudio", id, ValveSoundEvent.RotatingStarted);
                    yield return new WaitForSecondsRealtime(2f);
                    Call(Round, "ValveAudio", id, ValveSoundEvent.RotatingStopped);
                    break;
                }
            Step("compatGate");
            Call(Round, "CheatOpenActiveValves");
            yield return new WaitForSecondsRealtime(3f);
        }

        // 4-D.2: c1 makes noise and speech next to the host; packets / rings / lobby reactions are compared between builds.
        IEnumerator GateScenario()
        {
            yield return StartRound(1);
            if (OpenSpot(Host.transform.position, 3f, out var near)) Tp(1, near, Host.transform.position);
            yield return new WaitForSecondsRealtime(2.2f);
            foreach (var (signal, db) in new[] { ("keyboard", -30f), ("fan", -30f), ("white", -45f), ("voice", -25f), ("voice", -40f) })
            {
                Step("gate", $"\"signal\":{Q(signal)},\"dbfs\":{F(db)}");
                Cmd("1", "voice", $"{signal} {F(db)} 6");
                yield return new WaitForSecondsRealtime(7.5f);
            }
        }

        // 4-E: in the room before the round (voice reaches the CCTV figure), then in game (backdrop off).
        IEnumerator LobbyScenario()
        {
            Step("lobbyRoom", $"\"backdrop\":{Q(BackdropState())}");
            Cmd("*", "shot", "room");
            Shot("room");
            Cmd("1", "voice", "voice -25 3");
            yield return new WaitForSecondsRealtime(1.2f);
            Shot("roomVoice");
            Cmd("1", "shot", "roomVoice");
            yield return new WaitForSecondsRealtime(3f);
            yield return StartRound(1);
            Step("lobbyInGame", $"\"backdrop\":{Q(BackdropState())}");
            Cmd("1", "voice", "voice -25 3");
            yield return new WaitForSecondsRealtime(1.2f);
            Shot("inGameVoice");
            yield return new WaitForSecondsRealtime(3f);
            Step("lobbyInGameAfter", $"\"backdrop\":{Q(BackdropState())}");
        }

        static string BackdropState()
        {
            var t = T("Marco.Network.Lobby.LobbyCctvBackdrop");
            if (t == null) return "absent";
            var b = FindAnyObjectByType(t) as MonoBehaviour;
            if (b == null) return "no instance";
            var stage = Get(b, "stage") as GameObject;
            var camera = Get(b, "cameraFeed") as Camera;
            return $"enabled={b.enabled} active={Get(b, "active")} stage={(stage != null ? stage.activeSelf.ToString() : "null")} camera={(camera != null ? camera.enabled.ToString() : "null")} mic={Get(b, "microphoneStatus")} previewMic={(Get(b, "previewMic") != null)}";
        }

        IEnumerator LobbyIntroPhase()
        {
            Ev("intro", $"\"state\":{Q(IntroState())}");
            yield return new WaitForSecondsRealtime(2f);
            Shot("intro2s");
            Ev("intro", $"\"state\":{Q(IntroState())}");
            yield return new WaitForSecondsRealtime(4f);
            Shot("intro6s");
            Ev("intro", $"\"state\":{Q(IntroState())}");
            // A click cannot be sent to IMGUI from outside: the intro is ended through its own field (reported as such).
            var introType = T("Marco.Network.Lobby.LobbyIntro");
            var intro = introType != null ? FindAnyObjectByType(introType) as MonoBehaviour : null;
            if (intro != null && Get(intro, "IsPlaying") is bool playing && playing) { Set(intro, "finished", true); Ev("intro", "\"action\":\"finished set by reflection (no click possible)\""); }
            yield return new WaitForSecondsRealtime(1f);
            Shot("lobbyBeforeRoom");
            Ev("lobbyBeforeRoom", $"\"backdrop\":{Q(BackdropState())}");
        }

        // 4-F: three doors; the host shouts 4 m from a door, c1 watches from behind; both capture at the same instant.
        IEnumerator VisualScenario()
        {
            yield return StartRound(1);
            var doors = new List<(Vector3 center, float width)>();
            foreach (var part in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (part == null || part.GetType().Name != "PoolV12Part") continue;
                if (Get(part, "category") as string != "Door") continue;
                if (!(Get(part, "designPosition") is Vector3 pos) || !(Get(part, "openingWidth") is float w) || w < 1.9f) continue;
                if (Get(part, "floor") is int f && f >= 2) continue;
                doors.Add((pos, w));
            }
            doors = doors.OrderBy(d => d.center.x * 100 + d.center.z).ToList();
            int shown = 0;
            foreach (var door in doors)
            {
                if (shown >= 3) break;
                if (!OpenSpot(door.center, 4f, out var speak) || !OpenSpot(speak, 3f, out var watch, Mathf.Atan2(speak.z - door.center.z, speak.x - door.center.x))) continue;
                shown++;
                Tp(0, speak, door.center);
                Tp(1, watch, door.center);
                yield return new WaitForSecondsRealtime(2.5f);
                Step("door", $"\"n\":{shown},\"door\":{V(door.center)},\"width\":{F(door.width)},\"speaker\":{V(speak)},\"watcher\":{V(watch)}");
                StartCoroutine(Speak("steady", -10f, .3f));
                long at = Ms + 900;
                Cmd("1", "shotat", $"{at} door{shown}");
                StartCoroutine(ShotAt(at, $"door{shown}"));
                yield return new WaitForSecondsRealtime(3.5f);
                // Own ring afterglow: 15% → 0% over 12 s (host = the speaker).
                if (shown == 1)
                    for (int k = 0; k < 4; k++) { DumpRingProps("afterglow" + k); yield return new WaitForSecondsRealtime(4f); }
            }
            Step("doorsShown", $"\"count\":{shown}");
        }

        IEnumerator ShotAt(long at, string name)
        {
            while (Ms < at) yield return null;
            Shot(name);
            DumpRingProps(name);
        }

        // 4-G.4: rounds back to back for M minutes with voice and valve activity.
        IEnumerator SoakScenario()
        {
            float end = Time.realtimeSinceStartup + minutes * 60f;
            int r = 0;
            while (Time.realtimeSinceStartup < end)
            {
                r++;
                yield return StartRound(r);
                float roundEnd = Time.realtimeSinceStartup + 50f;
                while (Time.realtimeSinceStartup < roundEnd && Time.realtimeSinceStartup < end)
                {
                    Cmd("1", "voice", "voice -25 3");
                    yield return Speak("voice", -25f, 3f);
                    if (Get(Get(Round, "board"), "Valves") is IDictionary valves)
                        foreach (var key in valves.Keys.Cast<object>().ToList())
                        {
                            Call(Round, "ValveAudio", (ValveId)key, ValveSoundEvent.RotatingStarted);
                            yield return new WaitForSecondsRealtime(2f);
                            Call(Round, "ValveAudio", (ValveId)key, ValveSoundEvent.RotatingStopped);
                            break;
                        }
                    yield return new WaitForSecondsRealtime(5f);
                }
                Step("soakRound", $"\"n\":{r},\"players\":{NetworkPlayer.All.Count}");
            }
        }

        // ---------------------------------------------------------------- relay (4-A.5)
        IEnumerator RelayHost()
        {
            var relayType = T("Marco.Network.Lobby.UnityRelayLobby");
            var relay = relayType != null ? FindAnyObjectByType(relayType) as MonoBehaviour : null;
            if (relay == null) { Ev("relay", "\"result\":\"no UnityRelayLobby\""); yield return Finish(); yield break; }
            float deadline = Time.realtimeSinceStartup + 60;
            while (!(Get(relay, "Ready") is bool ready && ready) && Time.realtimeSinceStartup < deadline) yield return null;
            Ev("relay", $"\"ready\":{B(Get(relay, "Ready") is bool r && r)},\"status\":{Q(Get(relay, "Status") as string)}");
            if (!(Get(relay, "Ready") is bool ok && ok)) { yield return Finish(); yield break; }
            Call(relay, "CreateLobby", "HarnessHost");
            deadline = Time.realtimeSinceStartup + 60;
            string code = "";
            while (string.IsNullOrEmpty(code = Get(relay, "CurrentLobbyCode") as string) && Time.realtimeSinceStartup < deadline) yield return null;
            Ev("relay", $"\"code\":{Q(code)},\"status\":{Q(Get(relay, "Status") as string)}");
            File.WriteAllText(Path.Combine(syncDir, "relay_code.txt"), code ?? "");
            yield return WaitPlayers(2, 90);
            if (NetworkPlayer.All.Count >= 2) { yield return StartRound(1); yield return new WaitForSecondsRealtime(8f); }
            Ev("relay", $"\"status\":{Q(Get(relay, "Status") as string)},\"players\":{NetworkPlayer.All.Count}");
            yield return Finish();
        }

        IEnumerator RelayClient()
        {
            StartCoroutine(ClientCommands());
            var relayType = T("Marco.Network.Lobby.UnityRelayLobby");
            var relay = relayType != null ? FindAnyObjectByType(relayType) as MonoBehaviour : null;
            string file = Path.Combine(syncDir, "relay_code.txt");
            float deadline = Time.realtimeSinceStartup + 120;
            while (!File.Exists(file) && Time.realtimeSinceStartup < deadline) yield return null;
            string code = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
            while (relay != null && !(Get(relay, "Ready") is bool ready && ready) && Time.realtimeSinceStartup < deadline) yield return null;
            Ev("relay", $"\"code\":{Q(code)},\"ready\":{B(relay != null && Get(relay, "Ready") is bool r && r)},\"status\":{Q(relay != null ? Get(relay, "Status") as string : "no relay")}");
            if (relay != null && code.Length > 0) Call(relay, "JoinRoomCode", code);
            deadline = Time.realtimeSinceStartup + 60;
            while (NetworkPlayer.Local == null && Time.realtimeSinceStartup < deadline) yield return null;
            Ev("relay", $"\"joined\":{B(NetworkPlayer.Local != null)},\"status\":{Q(relay != null ? Get(relay, "Status") as string : "")}");
            if (NetworkPlayer.Local != null) WriteReady();
            while (!File.Exists(Path.Combine(syncDir, "done")) && Time.realtimeSinceStartup < deadline + 60) yield return null;
            Summary("relay client done");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
    }
}
#endif
