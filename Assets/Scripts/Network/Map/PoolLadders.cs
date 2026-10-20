using System.Collections.Generic;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Map
{
    // TEMPORARY until swimming exists: the pools have no stairs or ladders, so a player who walks
    // into the water (to reach Valve_B / Valve_E / the drains) could never climb out.
    // Each "PoolLadder_N" marker (inside the pool) has a "Top" child on the deck; pressing F near
    // the marker moves the local player onto the deck. Movement is client-authoritative, so the
    // owner moves itself and NetworkTransform carries it to everyone. Delete with swimming.
    public sealed class PoolLadders : MonoBehaviour
    {
        private const string LadderPrefix = "PoolLadder_";
        private const string TopName = "Top";

        [SerializeField] private float range = 1.5f;

        private readonly List<(Transform bottom, Transform top)> ladders = new();
        private Transform nearTop;

        private void Awake()
        {
            foreach (Transform child in transform)
            {
                if (!child.name.StartsWith(LadderPrefix)) continue;
                var top = child.Find(TopName);
                if (top != null) ladders.Add((child, top));
            }
        }

        private void Update()
        {
            nearTop = null;
            if (!TryGetLocalPlayer(out var player)) return;

            var position = player.transform.position;
            foreach (var (bottom, top) in ladders)
            {
                // Standing on the deck puts the capsule center above the top marker.
                if (position.y > top.position.y) continue;
                var offset = position - bottom.position;
                offset.y = 0;
                if (offset.sqrMagnitude > range * range) continue;
                nearTop = top;
                break;
            }

            if (nearTop != null && Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.F))
                player.GetComponent<PlayerMovement>().Teleport(nearTop.position + Vector3.up * GamePoints.PlayerStandHeight);
        }

        private void OnGUI()
        {
            if (nearTop == null) return;
            GUI.Label(new Rect(Screen.width / 2f - 100, Screen.height * 0.6f, 200, 30), "F: 풀 밖으로 나가기 (임시 사다리)");
        }

        private static bool TryGetLocalPlayer(out NetworkPlayer result)
        {
            foreach (var player in NetworkPlayer.All)
            {
                if (!player.IsOwner) continue;
                result = player;
                return true;
            }
            result = null;
            return false;
        }
    }
}
