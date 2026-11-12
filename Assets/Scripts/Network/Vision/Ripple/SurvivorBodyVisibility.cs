using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Vision
{
    // §16.1 [10-05] 어둠 속 캐릭터는 보이지 않는다 — on a survivor's screen the other living characters' real bodies
    // are not drawn; they appear only as source snapshots (§5.5-2, SourceSnapshots). Client only, local only: no
    // network traffic, no scene or prefab object (a runtime object made after the scene loads).
    //   on when RippleClientFeatures.SourceSnapshot, the local player is alive without an Echo body, and a round runs
    //   hides the 3rd-person model (every renderer under CharacterBody.BodyRoot) and the capsule fallback with
    //   Renderer.forceRenderingOff — the GameObjects stay active, so animators, colliders, controllers and the
    //   network keep running, and remote animators are kept on AlwaysAnimate so a snapshot bakes the real pose
    //   lobby, role reveal, result screen and Echo-body views are untouched (the switch condition is false there)
    // Echo bodies are already hidden from survivors by NetworkPlayer.ApplyLook (EchoBodyRules); only Alive players are touched.
    public sealed class SurvivorBodyVisibility : MonoBehaviour
    {
        sealed class Entry
        {
            public CharacterBody Character;
            public Transform Root;
            public bool Built, Hidden;
            public readonly List<Renderer> Renderers = new(8);
        }

        static SurvivorBodyVisibility instance;
        static readonly Dictionary<NetworkPlayer, Entry> entries = new(8);
        static readonly List<NetworkPlayer> gone = new(4);
        static readonly List<Renderer> found = new(16);
        static readonly List<Animator> animators = new(2);

        // For the checks and the temporary logs.
        public static int HiddenPlayers { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            try
            {
                if (instance != null) return;
                var go = new GameObject("Survivor Body Visibility");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<SurvivorBodyVisibility>();
            }
            catch (Exception exception) { DirectionHints.ReportFault("SurvivorBodyVisibility.Install", exception); }
        }

        // The survivor view of a running round (the same round test as the direction hint, §5.4 state D).
        public static bool Active()
        {
            if (!RippleClientFeatures.SourceSnapshot) return false;
            var local = NetworkPlayer.Local;
            var round = NetworkRound.Instance;
            if (local == null || round == null) return false;
            return local.Status == PlayerStatus.Alive && !local.HasEchoBody &&
                round.Mode != RoundMode.NotStarted && round.Result == RoundResult.InProgress;
        }

        void LateUpdate()
        {
            try { Apply(Active()); }
            catch (Exception exception) { DirectionHints.ReportFault("SurvivorBodyVisibility.LateUpdate", exception); }
        }

        static void Apply(bool active)
        {
            var local = NetworkPlayer.Local;
            var all = NetworkPlayer.All;
            int hidden = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var player = all[i];
                if (player == null) continue;
                bool hide = active && player != local && player.Status == PlayerStatus.Alive;
                if (!entries.TryGetValue(player, out var entry))
                {
                    if (!hide) continue;
                    entry = new Entry { Character = player.GetComponent<CharacterBody>() };
                    entries.Add(player, entry);
                }
                Refresh(player, entry);
                Set(entry, hide);
                if (hide) hidden++;
            }
            HiddenPlayers = hidden;
            // Despawned players: their renderers went with them; only the entry is dropped.
            gone.Clear();
            foreach (var pair in entries) if (pair.Key == null) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++) entries.Remove(gone[i]);
        }

        // The renderers of one player's 3rd-person body: the model (rebuilt on a role or look change) and the capsule.
        static void Refresh(NetworkPlayer player, Entry entry)
        {
            var root = entry.Character != null ? entry.Character.BodyRoot : null;
            if (entry.Built && root == entry.Root) return;
            if (entry.Hidden) Show(entry);
            entry.Renderers.Clear();
            entry.Root = root;
            entry.Built = true;
            // The capsule is the player's first child renderer (NetworkPlayer.Awake); it is drawn only without a model.
            var capsule = player.GetComponentInChildren<Renderer>(true);
            if (capsule != null && (root == null || !capsule.transform.IsChildOf(root))) entry.Renderers.Add(capsule);
            if (root == null) return;
            root.GetComponentsInChildren(true, found);
            for (int i = 0; i < found.Count; i++) if (found[i] != null) entry.Renderers.Add(found[i]);
            found.Clear();
            root.GetComponentsInChildren(true, animators);
            for (int i = 0; i < animators.Count; i++)
                if (animators[i] != null) animators[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animators.Clear();
        }

        static void Set(Entry entry, bool hide)
        {
            if (!hide)
            {
                if (entry.Hidden) Show(entry);
                return;
            }
            // Re-applied every frame: another local effect may hand a renderer back while the round runs.
            var list = entry.Renderers;
            for (int i = 0; i < list.Count; i++)
            {
                var renderer = list[i];
                if (renderer != null && !renderer.forceRenderingOff) renderer.forceRenderingOff = true;
            }
            entry.Hidden = true;
        }

        static void Show(Entry entry)
        {
            var list = entry.Renderers;
            for (int i = 0; i < list.Count; i++) if (list[i] != null) list[i].forceRenderingOff = false;
            entry.Hidden = false;
        }

        // The renderers SourceSnapshots bakes for a player (the same set this class hides). False when none is known.
        public static bool TryGetBody(NetworkPlayer player, out List<Renderer> renderers)
        {
            renderers = null;
            if (player == null) return false;
            if (!entries.TryGetValue(player, out var entry))
            {
                entry = new Entry { Character = player.GetComponent<CharacterBody>() };
                entries.Add(player, entry);
            }
            Refresh(player, entry);
            renderers = entry.Renderers;
            return entry.Renderers.Count > 0;
        }
    }
}
