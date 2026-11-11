using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Character;

using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network.Audio
{
    // Added only by NetworkPool_v1_2's round object. All clips are explicitly named resources.
    [DefaultExecutionOrder(100)]
    public sealed class RoundAudioPlayback : MonoBehaviour
    {
        public NetworkRound Round { get; set; }
        private sealed class Voice
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public float Until, Started, Gain;
            public bool Muffled;
            public int LoopKey = -1, ActionId = -1;
        }
        private readonly List<Voice> voices = new();
        private readonly Dictionary<string, AudioClip> clips = new();
        private AudioSource heartbeat, seekerDrone, diveAmbience;
        private double quietSince = double.NaN, fadeStartedAt = -1;
        private float droneFadeStart, droneEnvelope;
        private const float SeekerDroneMaxVolume = .15f;

        public void NotifyOtherSound()
        {
            if (double.IsNaN(quietSince)) return;
            double now = Time.timeAsDouble;
            if (droneEnvelope > 0 && fadeStartedAt < 0)
            {
                fadeStartedAt = now;
                droneFadeStart = droneEnvelope;
            }
            quietSince = now;
        }

        private void ResetSeekerDrone()
        {
            quietSince = double.NaN;
            fadeStartedAt = -1;
            droneFadeStart = droneEnvelope = 0;
            StopBed(seekerDrone);
        }

        private void TickSeekerDrone(bool eligible)
        {
            if (!eligible)
            {
                ResetSeekerDrone();
                return;
            }

            float handoff = Round.EndDroneBlend;
            if (Round.IsEndDronePhase)
            {
                quietSince = double.NaN;
                SetSeekerDroneVolume(droneEnvelope * (1f - handoff));
                return;
            }

            double now = Time.timeAsDouble;
            if (double.IsNaN(quietSince))
            {
                quietSince = now;
                droneFadeStart = 0;
            }
            float quiet = (float)(now - quietSince);
            if (fadeStartedAt >= 0)
            {
                float fade = Mathf.Clamp01((float)(now - fadeStartedAt) / .2f);
                droneEnvelope = droneFadeStart * (1f - fade);
                if (fade >= 1f) fadeStartedAt = -1;
            }
            else
            {
                droneEnvelope = quiet < 30f
                    ? .6f * Mathf.InverseLerp(20f, 30f, quiet)
                    : Mathf.Lerp(.6f, 1f, Mathf.InverseLerp(30f, 60f, quiet));
            }
            SetSeekerDroneVolume(droneEnvelope);
        }

        private void SetSeekerDroneVolume(float envelope)
        {
            if (envelope <= 0)
            {
                StopBed(seekerDrone);
                return;
            }
            if (seekerDrone == null)
            {
                var clip = Clip("SeekerDrone");
                if (clip == null) return;
                seekerDrone = CreateSource("SeekerDrone", true);
                seekerDrone.clip = clip;
                seekerDrone.loop = true;
            }
            seekerDrone.volume = envelope * SeekerDroneMaxVolume;
            if (!seekerDrone.isPlaying) seekerDrone.Play();
        }
        private readonly List<AudioClip> ownedLoops = new();

        private AudioClip Clip(string name)
        {
            if (clips.TryGetValue(name, out var result)) return result;
            result = Resources.Load<AudioClip>("MarcoSound/" + name);
            if (result != null && (name is "SeekerDrone" or "SurfaceSwim" or "DiveAmbience" or "ValveUnderwater"))
            {
                var original = result;
                result = name == "SeekerDrone" ? LoopClip.PrepareDrone(original) : LoopClip.Prepare(original, .2f);
                if (result != original) ownedLoops.Add(result);
            }
            if (result != null && (name is "Carpet" or "Mat" or "Metal" or "MetalStairs" or "WoodBench" || IsBreathClip(name)))
            {
                var original = result;
                result = LoopClip.TrimCue(original, IsBreathClip(name) ? .6f : .34f);
                if (result != original) ownedLoops.Add(result);
            }
            clips.Add(name, result);
            if (result == null) Debug.LogWarning("[MARCO][Audio] Missing clip: " + name, this);
            return result;
        }

        private static bool IsBreathClip(string name) => name is "Breath_Seeker" or "Breath_Runner";

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
            if (name == Marco.Network.Round.PulseSpec.NoAudio) return; // ring-only pulse (D9): explicitly silent
            var clip = Clip(name);
            if (clip == null) return;
            // The stair recording is hotter than the other footstep clips.
            if (name == "MetalStairs") volume *= .55f;
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
            voice.Muffled = muffled;
            ApplyVoiceFilter(voice);
            voice.Until = Time.time + (loopKey >= 0 ? 0.4f : Mathf.Min(duration > 0 ? duration : clip.length, clip.length));
            if (!voice.Source.isPlaying)
            {
                voice.Started = Time.time; voice.Gain = voice.Source.volume;
                voice.Source.time = Mathf.Clamp(startTime, 0, Mathf.Max(0, clip.length - .5f));
                if (name == "SurfaceSwim") voice.Source.volume = 0;
                voice.Source.Play();
            }
        }

        private static void ApplyVoiceFilter(Voice voice)
        {
            var local = NetworkPlayer.Local;
            bool diving = local != null && local.GetComponent<PlayerMovement>() is { IsDiving: true };
            voice.Filter.enabled = voice.Muffled || diving;
            voice.Filter.cutoffFrequency = diving ? 650f : 1000f;
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
            StopBed(heartbeat); StopBed(diveAmbience); ResetSeekerDrone(); steps.Clear();
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

        private void LateUpdate()
        {
            if (Round == null || !Round.IsClientInitialized || Round.Mode == RoundMode.NotStarted ||
                Round.Result != RoundResult.InProgress || NetworkPlayer.Local == null)
            {
                Clear(); return;
            }
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                ApplyVoiceFilter(voices[i]);
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
            var swim = local.GetComponent<PlayerMovement>();
            bool breathWarning = alive && swim != null && swim.IsDiving && swim.BreathSeconds <= 3f;
            if (breathWarning) heart = Mathf.Max(heart, swim.BreathSeconds <= 1f ? .65f : .4f);
            Bed(ref heartbeat, "Heartbeat", heart, 1f);
            Bed(ref diveAmbience, "DiveAmbience", alive && swim != null && swim.IsDiving ? .16f : 0f, .4f);
            if (heartbeat != null) heartbeat.pitch = breathWarning && swim.BreathSeconds <= 1f ? 1.6f : 1f;
            TickSeekerDrone(alive && local.Role == PlayerRole.Seeker && Round.StartPhase == RoundStartPhase.Released);
        }

        private sealed class StepState
        {
            public Vector3 Position;
            public readonly MovementPulseClock Clock = new();
            public int LastTile = -1, LastConcrete = -1;
            public bool Grounded, InAir, JumpPlayed;
            public float AirStarted, PeakHeight, SuppressStepsUntil;
            public string GroundSurface = "";
            public int GroundWater;
            public int PreviousWater;
            public int LastFootstepIndex;
            public float FallbackStepDistance;

        }
        private readonly Dictionary<NetworkPlayer, StepState> steps = new();
        private readonly SoundWorld footWorld = new();

        private void TickFootsteps(NetworkPlayer listener)
        {
            foreach (var player in NetworkPlayer.All)
            {
                var position = player.transform.position;

                if (!steps.TryGetValue(player, out var state))
                {
                    var initial = new StepState { Position = position };
                    if (player == listener && player.TryGetComponent<CharacterMotion>(out var initialMotion))
                        initial.LastFootstepIndex = initialMotion.FootstepIndex;
                    initial.Clock.Advance(position, Time.deltaTime, false, false, out _);
                    initial.Grounded = footWorld.TryFloorNear(player, out var initialFloor, .09f);
                    if (initial.Grounded) initial.GroundSurface = footWorld.Surface(initialFloor, position);
                    initial.GroundWater = footWorld.WaterState(player, out _);
                    initial.PreviousWater = initial.GroundWater;
                    steps[player] = initial;
                    continue;
                }
                Vector3 offset = position - state.Position;
                state.Position = position;
                float distance = new Vector2(offset.x, offset.z).magnitude;

                int water = footWorld.WaterState(player, out float waterLevel);
                bool onGround = footWorld.TryFloorNear(player, out var contactFloor, .09f);
                bool alive = player.Status == PlayerStatus.Alive && !player.IsEcho;
                bool teleported = offset.magnitude > Mathf.Max(1.5f, Time.deltaTime * 30f);
                bool enteredWater = alive && !teleported && state.PreviousWater == 0 && water != 0;
                state.PreviousWater = water;
                if (enteredWater)
                {
                    var impact = new Vector3(position.x, waterLevel, position.z);
                    if (footWorld.Hear(impact, listener, 6f, out var splashPoint, out float splashGain, out bool splashBlocked))
                    {
                        float strength = Mathf.Clamp(.5f + Mathf.Max(0, -offset.y) / Mathf.Max(Time.deltaTime, .001f) * .08f, .5f, 1f);
                        if (player != listener && splashGain > 0) NotifyOtherSound();
                        Play("WaterEntry", splashPoint, strength * splashGain, 0, -1, splashBlocked, -1);
                    }
                    state.InAir = state.JumpPlayed = false;
                    state.SuppressStepsUntil = Time.time + .35f;
                }
                if (!alive || teleported)
                {
                    state.InAir = state.JumpPlayed = false;
                    state.Grounded = onGround;
                }
                else
                {
                    if (!enteredWater && state.Grounded && !onGround)
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
                // Remote regular steps use the same server event as the shared movement ring.
                if (player != listener) continue;
                float horizontalSpeed = distance / Mathf.Max(Time.deltaTime, .0001f);
                bool fast = MovementPulseClock.IsFastMovement(player.Role, horizontalSpeed, water == 1);
                bool step = state.Clock.Advance(position, Time.deltaTime,
                    alive && !teleported && water != 2 && (onGround || water == 1), fast, out bool ring);
                if (Time.time < state.SuppressStepsUntil) { step = false; ring = false; }
                bool fastGrade = state.Clock.Fast;
                var rings = listener.GetComponentInChildren<WalkingFootRings>();
                if (rings != null)
                {
                    rings.SetLocalMoving(state.Clock.Moving);
                    if (ring)
                    {
                        var ringPoint = position;
                        ringPoint.y = water == 1 ? waterLevel + .025f :
                            player.GetComponent<CharacterController>().bounds.min.y + .025f;
                        rings.Show(player.PlayerId, ringPoint, fastGrade, player.Role == PlayerRole.Seeker);
                    }
                }
                if (water == 1 && step)
                    Play("SurfaceSwim", new Vector3(position.x, waterLevel, position.z), .55f, .8f, -1, false, -1);
                bool groundedStep = water == 0 && alive && !teleported && onGround &&
                    Time.time >= state.SuppressStepsUntil;
                bool footContact;
                if (player.TryGetComponent<CharacterMotion>(out var motion) && motion.HasFootstepCycle)
                {
                    int index = motion.FootstepIndex;
                    footContact = groundedStep && distance > .0001f && index > state.LastFootstepIndex;
                    state.LastFootstepIndex = index;
                    state.FallbackStepDistance = 0f;
                }
                else
                {
                    if (!groundedStep || !state.Clock.Moving) state.FallbackStepDistance = 0f;
                    else state.FallbackStepDistance += distance;
                    footContact = state.FallbackStepDistance >= 2f;
                    if (footContact) state.FallbackStepDistance %= 2f;
                }
                if (!footContact || contactFloor == null) continue;
                string surface = footWorld.Surface(contactFloor, position);
                var clip = FootstepClip(surface, state);
                if (clip != null)
                    Play(clip, position, .7f, fastGrade ? .8f : .4f, -1, false, -1);
            }
        }

        private void ContactSound(NetworkPlayer player, NetworkPlayer listener, StepState state,
            Vector3 position, int water, string surface, bool landing)
        {
            if (water == 2) return;
            bool wet = water == 1;
            string name = wet ? "SurfaceSwim" : FootstepClip(surface, state);
            if (name == null) return;
            float radius = landing ? 6f : 3f;
            if (!footWorld.Hear(position, listener, radius, out var point, out float gain, out bool blocked)) return;
            if (player != listener && gain > 0) NotifyOtherSound();
            var rings = listener.GetComponentInChildren<Marco.Network.Vision.WalkingFootRings>();
            if (rings != null)
            {
                var ringPoint = point;
                var body = (blocked ? listener : player).GetComponent<CharacterController>();
                ringPoint.y = body.bounds.min.y + .025f;
                rings.ShowContact(player.PlayerId, ringPoint, player.Role == PlayerRole.Seeker, landing);
            }

            int previousVoices = voices.Count;
            Play(name, point, (landing ? .9f : .7f) * gain, wet ? .4f : .38f, -1, blocked, -1, false, wet ? .08f : 0f);
            // Useful distinction in the hierarchy without changing the clip or adding an asset.
            if (voices.Count > previousVoices) voices[voices.Count-1].Source.name = (landing ? "Landing " : "Jump ") + player.PlayerId;
        }

        private static string FootstepClip(string surface, StepState state)
        {
            int count = SoundWorld.FootstepVariants(surface);
            if (count <= 1) return count == 1 ? surface : null;
            int previous = surface == "Tile" ? state.LastTile : state.LastConcrete;
            int choice = Random.Range(0, previous < 0 ? count : count - 1);
            if (previous >= 0 && choice >= previous) choice++;
            if (surface == "Tile") state.LastTile = choice; else state.LastConcrete = choice;
            return surface + "_" + choice;
        }

        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            Clear();
            if (heartbeat != null) Destroy(heartbeat.gameObject);
            if (seekerDrone != null) Destroy(seekerDrone.gameObject);
            if (diveAmbience != null) Destroy(diveAmbience.gameObject);
            foreach (var clip in ownedLoops) Destroy(clip);
            ownedLoops.Clear();
        }
    }
}
