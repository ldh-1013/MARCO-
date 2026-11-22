using Marco.Core.WinCondition;
using Marco.Network.Audio;
using UnityEngine;

namespace Marco.Network.Player
{
    public sealed partial class PlayerMovement
    {
        private bool waterFxReady;
        private int previousWaterFxState;
        private Vector3 previousWaterFxPosition;
        private float previousWaterFxFallSpeed, nextWaterWake, nextWaterSplash;

        // Observe replicated motion on every client, including the owner. No extra RPCs,
        // rigidbodies or changes to the authoritative swimming controller are needed.
        private void LateUpdate()
        {
            if (!IsClientInitialized || player == null || player.Status != PlayerStatus.Alive || player.IsEcho)
            { waterFxReady = false; return; }
            Vector3 position = transform.position;
            int state = swimWorld.WaterState(player, out float surface);
            Vector3 delta = position - previousWaterFxPosition;
            previousWaterFxPosition = position;
            float dt = Time.deltaTime;
            if (!waterFxReady || dt <= 0 || dt > .25f || delta.magnitude > Mathf.Max(1.5f, dt * 30f))
            {
                waterFxReady = true;
                previousWaterFxState = state;
                previousWaterFxFallSpeed = 0;
                nextWaterWake = Time.time + .25f;
                return; // Spawn, teleport and resume must not look like a high-speed impact.
            }
            float speed = new Vector2(delta.x, delta.z).magnitude / dt;
            float fallSpeed = Mathf.Max(0, -delta.y / dt);
            if (state != 0 && swimWorld.TryPool(position, out var pool))
            {
                Vector3 contact = new(position.x, surface + .025f, position.z);
                bool entered = previousWaterFxState == 0;
                bool crossed = previousWaterFxState != 0 && state != previousWaterFxState;
                if ((entered || crossed) && Time.time >= nextWaterSplash)
                {
                    float strength = entered
                        ? Mathf.Clamp(.5f + Mathf.Max(fallSpeed, previousWaterFxFallSpeed) * .1f, .5f, 1.4f)
                        : .35f;
                    PoolWaterEffects.Emit(contact, pool, strength, true, gameObject.scene);
                    nextWaterSplash = Time.time + .45f;
                    nextWaterWake = Time.time + .2f;
                }
                else if (state == 1 && speed > .18f && Time.time >= nextWaterWake)
                {
                    // Place successive rings just behind the swimmer to form a wake.
                    Vector3 heading = new Vector3(delta.x, 0, delta.z).normalized;
                    Vector3 wake = contact - heading * .22f;
                    wake.x = Mathf.Clamp(wake.x, pool.min.x + .02f, pool.max.x - .02f);
                    wake.z = Mathf.Clamp(wake.z, pool.min.z + .02f, pool.max.z - .02f);
                    PoolWaterEffects.Emit(wake, pool, Mathf.Clamp(speed * .09f, .12f, .4f), false, gameObject.scene);
                    nextWaterWake = Time.time + .24f;
                }
            }
            previousWaterFxState = state;
            previousWaterFxFallSpeed = fallSpeed;
        }
    }
}
