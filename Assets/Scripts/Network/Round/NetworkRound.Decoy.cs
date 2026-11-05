using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Player;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network.Round
{
    // Server authority: inventory, projectile collision and each fake step. Clients only present
    // listener-filtered sounds/rings. This throwable item is separate from the Echo ghost role.
    public sealed partial class NetworkRound
    {
        private readonly SyncList<bool> decoyPickups = new();
        private readonly HashSet<int> decoyHolders = new();
        private readonly List<DecoyFlight> decoyFlights = new();
        private readonly List<DecoyTrail> decoyTrails = new();
        private readonly List<GameObject> decoyPickupViews = new();
        private readonly Dictionary<int, GameObject> decoyFlightViews = new();
        private Material decoyMaterial;
        private Material decoyBeakMaterial, decoyEyeMaterial;
        private float decoyAccumulator;
        private int nextDecoyId;

        private sealed class DecoyFlight
        {
            public int Id;
            public int OwnerId;
            public Vector3 Position, Velocity, Heading;
            public float Age, SyncWait;
        }

        private sealed class DecoyTrail
        {
            public Vector3 Position, Heading;
            public int Step;
            public float Wait;
        }

        public float DecoyPickupRange => DecoyConfig.PickupRange;
        private bool DecoyRunning => startSchedule.HasInGameStarted && valves != null &&
            mode.Value != RoundMode.NotStarted && result.Value == RoundResult.InProgress;

        public bool TryFindDecoyPickup(Vector3 position, out int index)
        {
            index = -1;
            float best = DecoyConfig.PickupRange * DecoyConfig.PickupRange;
            for (int i = 0; i < decoyPickups.Count && i < DecoyConfig.PickupPositions.Length; i++)
            {
                float distance = (DecoyConfig.PickupPositions[i] - position).sqrMagnitude;
                if (!decoyPickups[i] || distance > best) continue;
                index = i;
                best = distance;
            }
            return index >= 0;
        }

        [Server]
        public bool TryPickupDecoy(int playerId, int index)
        {
            if (!DecoyRunning || !TryGetAliveRunner(playerId, out var player) ||
                decoyHolders.Contains(playerId) || index < 0 || index >= decoyPickups.Count ||
                index >= DecoyConfig.PickupPositions.Length || !decoyPickups[index] ||
                (player.transform.position - DecoyConfig.PickupPositions[index]).sqrMagnitude >
                    DecoyConfig.PickupRange * DecoyConfig.PickupRange ||
                !DecoyPathClear(player.transform.position + Vector3.up * .5f,
                    DecoyConfig.PickupPositions[index] + Vector3.up * .5f)) return false;
            decoyPickups[index] = false;
            decoyHolders.Add(playerId);
            player.SendDecoyCount(1);
            return true;
        }

        [Server]
        public bool TryThrowDecoy(int playerId, Vector3 aim)
        {
            if (!DecoyRunning || !TryGetAliveRunner(playerId, out var player) ||
                !decoyHolders.Contains(playerId) || player.IsTreatmentLocked || healingChannels.ContainsKey(playerId) ||
                !DecoyConfig.IsValidAim(aim.x, aim.y, aim.z)) return false;
            soundWorld ??= new SoundWorld();
            if (soundWorld.WaterState(player, out _) != 0) return false;
            // Only direction comes from the client. Spawn position, power and gravity are server-owned.
            aim.Normalize();
            Vector3 heading = Vector3.ProjectOnPlane(aim, Vector3.up).normalized;
            if (heading.sqrMagnitude < .01f) heading = player.transform.forward;
            Vector3 origin = DecoyBallistics.Origin(player.transform.position, player.IsCrouching);
            decoyFlights.Add(new DecoyFlight { Id = ++nextDecoyId, OwnerId = playerId, Position = origin,
                Velocity = DecoyBallistics.Velocity(aim), Heading = heading });
            DecoyFlightTarget(player.Owner, nextDecoyId, origin, true);
            decoyHolders.Remove(playerId);
            player.SendDecoyCount(0);
            return true;
        }

        private void ResetDecoysServer(bool available)
        {
            decoyFlights.Clear();
            decoyTrails.Clear();
            decoyAccumulator = 0;
            decoyHolders.Clear();
            decoyPickups.Clear();
            for (int i = 0; i < DecoyConfig.PickupPositions.Length; i++) decoyPickups.Add(available && SoundMap);
            foreach (var player in NetworkPlayer.All) player.SendDecoyCount(0);
            if (IsServerInitialized) ClearDecoyObservers();
        }

        private void TickDecoysServer()
        {
            // Lost inventory is never restored by a role cheat or reconnection in the same round.
            foreach (var player in NetworkPlayer.All)
                if ((player.Status != PlayerStatus.Alive || player.Role != PlayerRole.Runner) &&
                    decoyHolders.Remove(player.PlayerId)) player.SendDecoyCount(0);
            if (!DecoyRunning)
            {
                if (decoyFlights.Count > 0 || decoyTrails.Count > 0)
                {
                    decoyFlights.Clear(); decoyTrails.Clear(); ClearDecoyObservers();
                }
                decoyAccumulator = 0;
                return;
            }
            decoyAccumulator += Mathf.Min(Time.deltaTime, .1f);
            while (decoyAccumulator >= DecoyConfig.PhysicsStep)
            {
                decoyAccumulator -= DecoyConfig.PhysicsStep;
                StepDecoys(DecoyConfig.PhysicsStep);
            }
        }

        private void StepDecoys(float dt)
        {
            // Tick existing trails first: newly landed devices emit once immediately, not twice.
            for (int i = decoyTrails.Count - 1; i >= 0; i--)
            {
                var trail = decoyTrails[i];
                trail.Wait -= dt;
                if (trail.Wait > 0) continue;
                Vector3 next = trail.Position + trail.Heading * DecoyConfig.StepDistance;
                if (!DecoyPathClear(trail.Position + Vector3.up * .2f, next + Vector3.up * .2f) ||
                    !TryDecoyFloor(next, out var floor)) { decoyTrails.RemoveAt(i); continue; }
                trail.Position = floor.point;
                trail.Step++;
                EmitDecoyStep(trail.Position, floor.collider, false, trail.Step);
                trail.Wait += DecoyConfig.StepInterval;
                if (trail.Step >= DecoyConfig.MovingSteps) decoyTrails.RemoveAt(i);
            }

            for (int i = decoyFlights.Count - 1; i >= 0; i--)
            {
                var flight = decoyFlights[i];
                flight.Age += dt;
                if (flight.Age > DecoyConfig.MaxFlightSeconds)
                { SendDecoyFlight(flight, false); decoyFlights.RemoveAt(i); continue; }
                if (DecoyBallistics.Step(ref flight.Position, ref flight.Velocity, out var hit))
                {
                    EmitDecoyStep(hit.point, hit.collider, true, 0);
                    decoyTrails.Add(new DecoyTrail { Position = hit.point, Heading = flight.Heading,
                        Wait = DecoyConfig.StepInterval });
                    SendDecoyFlight(flight, false);
                    decoyFlights.RemoveAt(i);
                    continue;
                }
                flight.SyncWait -= dt;
                if (flight.SyncWait <= 0)
                {
                    flight.SyncWait = DecoyConfig.FlightSyncSeconds;
                    SendDecoyFlight(flight, true);
                }
                // In flight only the thrower sees the device; exact coordinates cannot leak to the Seeker.
                // The authoritative trail is heard through the same SoundWorld as real footsteps.
            }
        }

        private static bool DecoyPathClear(Vector3 from, Vector3 to) => !DecoyBallistics.Cast(from, to - from, out _);

        private static bool TryDecoyFloor(Vector3 point, out RaycastHit closest)
        {
            closest = default;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * .4f, Vector3.down, .8f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < .5f || hit.collider.GetComponentInParent<NetworkPlayer>() != null ||
                    hit.distance >= best) continue;
                closest = hit; best = hit.distance;
            }
            return best < float.MaxValue;
        }

        private void EmitDecoyStep(Vector3 origin, Collider floor, bool impact, int step)
        {
            soundWorld ??= new SoundWorld();
            string surface = soundWorld.Surface(floor, origin);
            if (surface != "Tile") surface = "Concrete";
            float radius = (impact ? DecoyConfig.ImpactRadius : DecoyConfig.FootstepRadius) *
                (surface == "Tile" ? 1.3f : 1f);
            string clip = surface + "_" + step % (surface == "Tile" ? 3 : 4);
            foreach (var listener in NetworkPlayer.All)
            {
                if (listener.Status != PlayerStatus.Alive || listener.IsEcho ||
                    !soundWorld.Hear(origin + Vector3.up * .025f, listener, radius,
                        out var point, out float gain, out bool blocked)) continue;
                // No source id or exact source coordinates are sent through a wall.
                DecoyPulseTarget(listener.Owner, point, clip, gain, blocked,
                    impact ? DecoyConfig.ImpactRingRadius : DecoyConfig.FootstepRingRadius);
            }
        }

        [TargetRpc]
        private void DecoyPulseTarget(NetworkConnection connection, Vector3 point, string clip,
            float gain, bool blocked, float ringRadius)
        {
            if (soundPlayback != null) soundPlayback.Play(clip, point, .7f * gain, .38f, -1, blocked, -1);
            if (!blocked && NetworkPlayer.Local != null)
                NetworkPlayer.Local.GetComponentInChildren<WalkingFootRings>()?.EmitWaterPulse(point, ringRadius);
        }

        private void UpdateDecoyViews()
        {
            if (!SoundMap) return;
            if (decoyMaterial == null)
            {
                // Pickups need their own readable colour in darkness; this does not light the surroundings.
                var shader = Shader.Find("Marco/WalkingFootRing") ?? Shader.Find("PoolV12/BlockoutSurface");
                if (shader == null) return;
                decoyMaterial = CreateBandageMaterial(shader, new Color(1f, .78f, .08f));
                decoyBeakMaterial = CreateBandageMaterial(shader, new Color(1f, .32f, .03f));
                decoyEyeMaterial = CreateBandageMaterial(shader, new Color(.035f, .025f, .015f));
            }
            while (decoyPickupViews.Count < DecoyConfig.PickupPositions.Length)
            {
                var root = CreateDecoyDuck("오리 투척 장치");
                decoyPickupViews.Add(root);
            }
            for (int i = 0; i < decoyPickupViews.Count; i++)
            {
                var view = decoyPickupViews[i];
                view.SetActive(i < decoyPickups.Count && decoyPickups[i]);
                view.transform.position = DecoyConfig.PickupPositions[i] + Vector3.up * DecoyConfig.PickupVisualHeight;
                view.transform.rotation = Quaternion.Euler(0, DecoyConfig.PickupYaw[i], 0);
            }
        }

        private GameObject CreateDecoyDuck(string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            var visual = new GameObject("Duck visual").transform;
            visual.SetParent(root.transform, false);
            DuckPart(visual, "노란 몸통", new Vector3(.25f, .16f, .22f), new Vector3(0, .09f, 0), decoyMaterial);
            DuckPart(visual, "머리", Vector3.one * .12f, new Vector3(0, .215f, -.085f), decoyMaterial);
            CreateBandageCube(visual, "주황 부리", new Vector3(.09f, .035f, .085f), new Vector3(0, .195f, -.16f), decoyBeakMaterial);
            DuckPart(visual, "왼쪽 날개", new Vector3(.04f, .09f, .13f), new Vector3(-.115f, .1f, .015f), decoyMaterial);
            DuckPart(visual, "오른쪽 날개", new Vector3(.04f, .09f, .13f), new Vector3(.115f, .1f, .015f), decoyMaterial);
            DuckPart(visual, "왼쪽 눈", Vector3.one * .025f, new Vector3(-.047f, .232f, -.125f), decoyEyeMaterial);
            DuckPart(visual, "오른쪽 눈", Vector3.one * .025f, new Vector3(.047f, .232f, -.125f), decoyEyeMaterial);
            return root;
        }

        private static void DuckPart(Transform parent, string name, Vector3 scale, Vector3 position, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            part.name = name; part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            part.GetComponent<Collider>().enabled = false;
            var renderer = part.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        [ObserversRpc]
        private void ClearDecoyObservers()
        {
            ClearDecoyFlightViews();
            // Rings are shared with movement: clear them on round teardown to prevent stale fake cues.
            if (NetworkPlayer.Local != null)
                NetworkPlayer.Local.GetComponentInChildren<WalkingFootRings>()?.ClearPulses();
        }

        private void ClearDecoyViews()
        {
            ClearDecoyFlightViews();
            foreach (var view in decoyPickupViews) if (view != null) Destroy(view);
            decoyPickupViews.Clear();
            if (decoyMaterial != null) Destroy(decoyMaterial);
            if (decoyBeakMaterial != null) Destroy(decoyBeakMaterial);
            if (decoyEyeMaterial != null) Destroy(decoyEyeMaterial);
            decoyMaterial = decoyBeakMaterial = decoyEyeMaterial = null;
        }

        private void SendDecoyFlight(DecoyFlight flight, bool active)
        {
            if (NetworkPlayer.TryGet(flight.OwnerId, out var owner))
                DecoyFlightTarget(owner.Owner, flight.Id, flight.Position, active);
        }

        [TargetRpc]
        private void DecoyFlightTarget(NetworkConnection connection, int id, Vector3 position, bool active)
        {
            if (!active)
            {
                if (decoyFlightViews.Remove(id, out var old) && old != null) Destroy(old);
                return;
            }
            if (!decoyFlightViews.TryGetValue(id, out var view))
            {
                if (decoyMaterial == null) UpdateDecoyViews();
                if (decoyMaterial == null) return;
                view = CreateDecoyDuck("투척 중인 오리");
                var visual = view.transform.GetChild(0);
                visual.localScale = Vector3.one * .55f;
                visual.localPosition = Vector3.down * .075f;
                decoyFlightViews[id] = view;
            }
            view.transform.position = position;
        }

        private void ClearDecoyFlightViews()
        {
            foreach (var view in decoyFlightViews.Values) if (view != null) Destroy(view);
            decoyFlightViews.Clear();
        }
    }
}
