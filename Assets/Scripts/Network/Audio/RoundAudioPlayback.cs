using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Character;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Audio
{
    // Added only by NetworkPool_v1_2's round object. All clips are explicitly named resources.
    public sealed class RoundAudioPlayback : MonoBehaviour
    {
        public NetworkRound Round { get; set; }
        private sealed class Voice
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public float Until, Started, Gain;
            public int LoopKey = -1, ActionId = -1;
        }
        private readonly List<Voice> voices = new();
        private readonly Dictionary<string, AudioClip> clips = new();
        private AudioSource heartbeat, seekerDrone;
        private readonly List<AudioClip> ownedLoops = new();

        private AudioClip Clip(string name)
        {
            if (clips.TryGetValue(name, out var result)) return result;
            result = Resources.Load<AudioClip>("MarcoSound/" + name);
            if (result != null && (name == "SeekerDrone" || name == "SurfaceSwim"))
            {
                var original = result;
                result = LoopClip.Prepare(original, name == "SeekerDrone" ? .75f : .2f);
                if (result != original) ownedLoops.Add(result);
            }
            clips.Add(name, result);
            if (result == null) Debug.LogWarning("[MARCO][Audio] Missing clip: " + name, this);
            return result;
        }

        private AudioSource CreateSource(string label, bool local)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = local ? 0f : 1f;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            // Server owns attenuation; Unity only pans the perceived position.
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0, 1, 1));
            source.maxDistance = 200f;
            return source;
        }

        public void Play(string name, Vector3 point, float volume, float duration, int loopKey,
            bool muffled, int actionId, bool local = false, float startTime = 0)
        {
            var clip = Clip(name);
            if (clip == null) return;
            Voice voice = null;
            if (loopKey >= 0)
                foreach (var candidate in voices)
                    if (candidate.LoopKey == loopKey) { voice = candidate; break; }
            if (voice == null)
            {
                if (voices.Count >= 48) return;
                var source = CreateSource("SFX " + name, local);
                if (loopKey >= 200000) source.volume = 0f;
                voice = new Voice { Source = source, Filter = source.gameObject.AddComponent<AudioLowPassFilter>(),
                    LoopKey = loopKey, ActionId = actionId };
                voices.Add(voice);
                source.clip = clip;
                source.loop = loopKey >= 0;
            }
            voice.Source.transform.position = point;
            voice.Source.volume = loopKey >= 200000
                ? Mathf.MoveTowards(voice.Source.volume, Mathf.Clamp01(volume), Time.deltaTime * 4f)
                : Mathf.Clamp01(volume);
            voice.Filter.enabled = muffled;
            voice.Filter.cutoffFrequency = 1000f;
            voice.Until = Time.time + (loopKey >= 0 ? 0.4f : Mathf.Min(duration > 0 ? duration : clip.length, clip.length));
            if (!voice.Source.isPlaying)
            {
                voice.Started = Time.time; voice.Gain = voice.Source.volume;
                voice.Source.time = Mathf.Clamp(startTime, 0, Mathf.Max(0, clip.length - .5f));
                if (name == "SurfaceSwim") voice.Source.volume = 0;
                voice.Source.Play();
            }
        }

        public void Stop(int key, bool healing)
        {
            for (int i = voices.Count - 1; i >= 0; i--)
                if (healing ? voices[i].ActionId == key : voices[i].LoopKey == key) Remove(i);
        }

        private void Remove(int index)
        {
            var voice = voices[index];
            if (voice.Source != null) { voice.Source.Stop(); Destroy(voice.Source.gameObject); }
            voices.RemoveAt(index);
        }

        public void Clear()
        {
            for (int i = voices.Count - 1; i >= 0; i--) Remove(i);
            StopBed(heartbeat); StopBed(seekerDrone); steps.Clear();
        }

        private static void StopBed(AudioSource source)
        {
            if (source == null) return;
            source.Stop(); source.volume = 0f;
        }

        private void Bed(ref AudioSource source, string name, float target, float fadeSeconds)
        {
            if (source == null)
            {
                if (target <= 0) return;
                var clip = Clip(name);
                if (clip == null) return;
                source = CreateSource(name, true);
                source.clip = clip; source.loop = true; source.volume = 0;
            }
            source.volume = Mathf.Lerp(source.volume, target, 1f - Mathf.Exp(-Time.deltaTime * 4f / Mathf.Max(.1f, fadeSeconds)));
            if (target > 0 && !source.isPlaying) source.Play();
            else if (target <= 0 && source.volume < .001f) StopBed(source);
        }

        private void Update()
        {
            if (Round == null || !Round.IsClientInitialized || Round.Mode == RoundMode.NotStarted ||
                Round.Result != RoundResult.InProgress || NetworkPlayer.Local == null)
            {
                Clear(); return;
            }
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                float remaining = voices[i].Until - Time.time;
                if (voices[i].Source.clip != null && voices[i].Source.clip.name.StartsWith("SurfaceSwim") && voices[i].LoopKey < 0)
                    voices[i].Source.volume = voices[i].Gain * Mathf.Clamp01((Time.time - voices[i].Started)/.02f) * Mathf.Clamp01(remaining/.04f);
                if (remaining <= 0) Remove(i);
                else if (remaining < (voices[i].LoopKey >= 0 ? .08f : .025f))
                    voices[i].Source.volume *= Mathf.Clamp01(remaining / (voices[i].LoopKey >= 0 ? .08f : .025f));
            }
            var local = NetworkPlayer.Local;
            bool alive = local.Status == PlayerStatus.Alive && !local.IsEcho;
            if (alive) TickFootsteps(local); else steps.Clear();
            float heart = 0;
            if (alive && local.Role == PlayerRole.Runner)
                foreach (var player in NetworkPlayer.All)
                    if (player.Role == PlayerRole.Seeker && player.Status == PlayerStatus.Alive)
                        heart = Mathf.Max(heart, 0.35f * Mathf.Clamp01(1f - Vector3.Distance(local.transform.position, player.transform.position) / 8f));
            Bed(ref heartbeat, "Heartbeat", heart, 1f);
            Bed(ref seekerDrone, "SeekerDrone", alive && local.Role == PlayerRole.Seeker ? .15f : 0f, 2f);
        }

        private sealed class StepState
        {
            public Vector3 Position;
            public int Index;
            public int LastTile = -1, LastConcrete = -1;
            public bool Running;
            public bool Grounded, InAir, JumpPlayed;
            public float AirStarted, PeakHeight, SuppressStepsUntil;
            public string GroundSurface = "";
            public int GroundWater;
            public float MovedAt, Timer, LastWaterStep = -10f;
        }
        private readonly Dictionary<NetworkPlayer, StepState> steps = new();
        private readonly SoundWorld footWorld = new();

        private void TickFootsteps(NetworkPlayer listener)
        {
            foreach (var player in NetworkPlayer.All)
            {
                var position = player.transform.position;
                var motion = player.GetComponent<CharacterMotion>();
                bool animated = motion != null && motion.HasFootstepCycle;
                int index = animated ? motion.FootstepIndex : 0;
                bool running = animated && motion.FootstepRunning;
                if (!steps.TryGetValue(player, out var state))
                {
                    var initial = new StepState { Position = position, Index = index, Running = running };
                    initial.Grounded = footWorld.TryFloorNear(player, out var initialFloor, .09f);
                    if (initial.Grounded) initial.GroundSurface = footWorld.Surface(initialFloor, position);
                    initial.GroundWater = footWorld.WaterState(player, out _);
                    steps[player] = initial;
                    continue;
                }
                Vector3 offset = position - state.Position;
                state.Position = position;
                float distance = new Vector2(offset.x, offset.z).magnitude;
                bool eligible = player.Status == PlayerStatus.Alive && !player.IsEcho &&
                    distance < 15f * Mathf.Max(Time.deltaTime, .02f) && Mathf.Abs(offset.y) < .4f;
                if (eligible && distance / Mathf.Max(Time.deltaTime, .0001f) > .05f) state.MovedAt = Time.time;
                bool moving = eligible && Time.time - state.MovedAt < .15f;
                bool step = moving && animated && index != state.Index && running == state.Running;
                state.Index = index; state.Running = running;
                if (!animated && moving)
                {
                    state.Timer += Time.deltaTime;
                    float interval = player.GetComponent<PlayerMovement>() is { IsSprinting: true } ? .3f : .5f;
                    if (state.Timer >= interval) { state.Timer %= interval; step = true; }
                }
                else if (!moving) state.Timer = 0;
                int water = footWorld.WaterState(player, out float waterLevel);
                bool onGround = footWorld.TryFloorNear(player, out var contactFloor, .09f);
                bool alive = player.Status == PlayerStatus.Alive && !player.IsEcho;
                bool teleported = offset.magnitude > Mathf.Max(1.5f, Time.deltaTime * 30f);
                if (!alive || teleported)
                {
                    state.InAir = state.JumpPlayed = false;
                    state.Grounded = onGround;
                }
                else
                {
                    if (state.Grounded && !onGround)
                    {
                        state.InAir = true; state.AirStarted = Time.time; state.PeakHeight = position.y;
                        state.JumpPlayed = offset.y / Mathf.Max(Time.deltaTime, .0001f) > .5f;
                        if (state.JumpPlayed)
                        {
                            ContactSound(player, listener, state, position, state.GroundWater, state.GroundSurface, false);
                            state.SuppressStepsUntil = Time.time + .15f;
                        }
                    }
                    if (state.InAir) state.PeakHeight = Mathf.Max(state.PeakHeight, position.y);
                    if (state.InAir && onGround)
                    {
                        if (state.JumpPlayed || Time.time - state.AirStarted > .12f || state.PeakHeight - position.y > .2f)
                        {
                            ContactSound(player, listener, state, position, water, footWorld.Surface(contactFloor, position), true);
                            state.SuppressStepsUntil = Time.time + .15f;
                        }
                        state.InAir = state.JumpPlayed = false;
                    }
                    state.Grounded = onGround;
                }
                if (onGround)
                {
                    state.GroundSurface = footWorld.Surface(contactFloor, position);
                    state.GroundWater = water;
                }
                if (!alive || !onGround || Time.time < state.SuppressStepsUntil) step = false;
                int waterKey = 200000 + player.PlayerId;
                if (step) state.LastWaterStep = Time.time;
                if (water == 1 && moving && footWorld.Hear(new Vector3(position.x, waterLevel, position.z), listener,
                    6f, out var waterPoint, out float waterGain, out bool waterBlocked))
                {
                    // Continuous recording with gentle step accents, never hard-cut every 0.4s.
                    float strength = .38f + .17f * Mathf.Exp(-(Time.time-state.LastWaterStep)*8f);
                    Play("SurfaceSwim", waterPoint, strength * waterGain, 0, waterKey, waterBlocked, -1);
                }
                else
                    foreach (var voice in voices)
                        if (voice.LoopKey == waterKey) voice.Until = Mathf.Min(voice.Until, Time.time + .08f);
                // Water replaces dry footsteps, including when fully submerged (silent).
                if (water != 0 || !step || !footWorld.TryFloor(player, out var floor)) continue;
                string surface = footWorld.Surface(floor, position);
                if (surface != "Tile" && surface != "Concrete") continue;
                if (!footWorld.Hear(position, listener, (running ? 6f : 2f) * (surface == "Tile" ? 1.3f : 1f),
                    out var point, out float gain, out bool blocked)) continue;
                int count = surface == "Tile" ? 3 : 4;
                int previous = surface == "Tile" ? state.LastTile : state.LastConcrete;
                int choice = Random.Range(0, previous < 0 ? count : count - 1);
                if (previous >= 0 && choice >= previous) choice++;
                if (surface == "Tile") state.LastTile = choice; else state.LastConcrete = choice;
                Play(surface + "_" + choice, point, .7f * gain, .38f, -1, blocked, -1);
            }
        }

        private void ContactSound(NetworkPlayer player, NetworkPlayer listener, StepState state,
            Vector3 position, int water, string surface, bool landing)
        {
            if (water == 2) return;
            bool wet = water == 1;
            if (!wet && surface != "Tile" && surface != "Concrete") return;
            float radius = landing ? 6f : 3f;
            if (!footWorld.Hear(position, listener, radius, out var point, out float gain, out bool blocked)) return;
            string name;
            if (wet) name = "SurfaceSwim";
            else
            {
                int count = surface == "Tile" ? 3 : 4;
                int previous = surface == "Tile" ? state.LastTile : state.LastConcrete;
                int choice = Random.Range(0, previous < 0 ? count : count - 1);
                if (previous >= 0 && choice >= previous) choice++;
                if (surface == "Tile") state.LastTile = choice; else state.LastConcrete = choice;
                name = surface + "_" + choice;
            }
            int previousVoices = voices.Count;
            Play(name, point, (landing ? .9f : .7f) * gain, wet ? .4f : .38f, -1, blocked, -1, false, wet ? .08f : 0f);
            // Useful distinction in the hierarchy without changing the clip or adding an asset.
            if (voices.Count > previousVoices) voices[voices.Count-1].Source.name = (landing ? "Landing " : "Jump ") + player.PlayerId;
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            Clear();
            if (heartbeat != null) Destroy(heartbeat.gameObject);
            if (seekerDrone != null) Destroy(seekerDrone.gameObject);
            foreach (var clip in ownedLoops) Destroy(clip);
            ownedLoops.Clear();
        }
    }
}
