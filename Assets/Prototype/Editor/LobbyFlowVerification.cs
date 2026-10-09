using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Marco.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Batch-only integration check: -executeMethod LobbyFlowVerification.Run (without -quit).
[InitializeOnLoad]
public static class LobbyFlowVerification
{
    private const string ActiveKey = "MARCO.LobbyFlowVerification.Active";
    static LobbyFlowVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ActiveKey, false))
                new GameObject("Lobby flow verification").AddComponent<LobbyFlowProbe>();
        };
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch project for this integration check.");
        CombinedSceneVerification.Run();
        // Start from the game scene itself to exercise the lobby-first entry guard.
        EditorSceneManager.OpenScene("Assets/Prototype/Scenes/Prototype_Game.unity");
        // Seed a stale role and keep statics across Play entry: startup must still reset it.
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        MatchLaunchContext.Begin(PrototypePlayerRole.Survivor);
        SessionState.SetBool(ActiveKey, true);
        EditorApplication.EnterPlaymode();
    }

    public static void Finish(bool success, Exception error = null)
    {
        SessionState.SetBool(ActiveKey, false);
        if (success) Debug.Log("PASS: lobby host/guest start, role ownership, session survival, disconnect and lobby return");
        else Debug.LogError("FAIL: lobby flow verification: " + error);
        EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class LobbyFlowProbe : MonoBehaviour
{
    private TcpListener fakeHost;
    private TcpClient fakePeer;
    private TcpClient secondPeer;
    private int checks;
    private void Awake() { DontDestroyOnLoad(gameObject); }

    private IEnumerator Start()
    {
        // Catch failures raised by nested coroutine steps, including socket task errors.
        var stack = new System.Collections.Generic.Stack<IEnumerator>();
        stack.Push(Verify());
        while (stack.Count > 0)
        {
            object current = null;
            bool moved = false;
            Exception failure = null;
            try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
            catch (Exception e) { failure = e; }
            if (failure != null) { Cleanup(); LobbyFlowVerification.Finish(false, failure); yield break; }
            if (!moved) { stack.Pop(); continue; }
            if (current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
        Cleanup();
        Debug.Log("Lobby flow assertions: " + checks);
        LobbyFlowVerification.Finish(true);
    }

    private IEnumerator Verify()
    {
        VerifyRoleAssignmentProtocol();
        yield return null;
        yield return Until(() => SceneManager.GetActiveScene().name == MatchLaunchContext.LobbyScene,
            "Direct game scene entry redirects to lobby");
        Check(!MatchLaunchContext.IsActive, "Direct entry has no game session");
        var room = FindAnyObjectByType<RoomSessionPrototype>();
        Check(room != null, "Lobby contains room session");
        // Choose an available local port so this test cannot collide with someone's room.
        fakeHost = new TcpListener(IPAddress.Loopback, 0); fakeHost.Start();
        int port = ((IPEndPoint)fakeHost.LocalEndpoint).Port;
        fakeHost.Stop(); fakeHost = null;
        Set(room, "port", port); Set(room, "advertisedIp", "127.0.0.1");
        Call(room, "CreateRoom");
        Check(room.IsHost && room.MemberCount == 1 && room.CanStartGame, "Create room enables host start");
        fakePeer = new TcpClient();
        var connect = fakePeer.ConnectAsync(IPAddress.Loopback, port);
        yield return Until(() => connect.IsCompleted, "Guest TCP connection"); connect.GetAwaiter().GetResult();
        var writer = new StreamWriter(fakePeer.GetStream()) { AutoFlush = true };
        var reader = new StreamReader(fakePeer.GetStream());
        writer.WriteLine("NAME R3Vlc3Q=");
        var roster = reader.ReadLineAsync();
        yield return Until(() => room.MemberCount == 2 && roster.IsCompleted, "Guest roster handshake");
        Check(roster.GetAwaiter().GetResult().StartsWith("MARCO/2 ROSTER "), "Host publishes roster");
        secondPeer = new TcpClient();
        connect = secondPeer.ConnectAsync(IPAddress.Loopback, port);
        yield return Until(() => connect.IsCompleted, "Second guest TCP connection"); connect.GetAwaiter().GetResult();
        var secondWriter = new StreamWriter(secondPeer.GetStream()) { AutoFlush = true };
        var secondReader = new StreamReader(secondPeer.GetStream());
        secondWriter.WriteLine("NAME R3Vlc3Qy");
        roster = reader.ReadLineAsync(); var secondRoster = secondReader.ReadLineAsync();
        yield return Until(() => room.MemberCount == 3 && roster.IsCompleted && secondRoster.IsCompleted, "Three-member roster handshake");
        roster.GetAwaiter().GetResult(); secondRoster.GetAwaiter().GetResult();
        var start = reader.ReadLineAsync();
        var secondStart = secondReader.ReadLineAsync();
        room.StartGame();
        yield return Until(() => SceneManager.GetActiveScene().name == MatchLaunchContext.GameScene && start.IsCompleted, "Host game scene transition");
        Check(RoomLaunchProtocol.TryParseMatch(start.GetAwaiter().GetResult(), out int guestId, out var assignment), "Guest receives full participant assignment");
        var guestRole = Array.Find(assignment, member => member.Id == guestId).Role;
        Check(Array.FindAll(assignment, m => m.Role == PrototypePlayerRole.Hunter).Length == 1 && assignment.Length == 3,
            "Exactly one hunter among all three assigned participants");
        yield return Until(() => secondStart.IsCompleted, "Second guest receives start");
        Check(RoomLaunchProtocol.TryParseMatch(secondStart.Result, out int secondId, out var secondAssignment) && secondId != guestId
            && secondAssignment.Length == 3, "Guests receive distinct authoritative IDs");
        Check(room == FindAnyObjectByType<RoomSessionPrototype>() && room.GameStarted, "Session survives lobby unload");
        yield return VerifyLocalAssetOwner();
        ValidateGame();
        yield return VerifyHostPose(room, writer, reader, guestId);
        bool relayed = false;
        for (int attempt = 0; attempt < 30 && !relayed; attempt++)
        {
            var line = secondReader.ReadLineAsync();
            yield return Until(() => line.IsCompleted, "Second guest receives relay stream");
            relayed = PlayerPoseProtocol.TryParse(line.Result, out var forwarded) && forwarded.PlayerId == guestId && forwarded.Sequence == 2;
        }
        Check(relayed, "Host relays one guest's gaze/action state to another guest over TCP");
        yield return VerifyHostRoleSwitch(room, writer, reader, secondReader, guestId, secondId);
        var departed = Array.Find(FindObjectsByType<PlayerPoseReplicator>(), p => p.PlayerId == guestId);
        fakePeer.Close(); fakePeer = null;
        yield return Until(() => departed == null && room.MemberCount == 2, "Host removes disconnected guest body without ending the other connection");
        bool leftRelayed = false;
        for (int attempt = 0; attempt < 30 && !leftRelayed; attempt++)
        {
            var line = secondReader.ReadLineAsync();
            yield return Until(() => line.IsCompleted, "Second guest receives leave stream");
            leftRelayed = line.Result == "MARCO/3 LEFT " + guestId;
        }
        Check(leftRelayed, "Host forwards participant leave to other guest");
        room.StartGame();
        Check(room.GameStarted && !room.CanStartGame, "Duplicate start is ignored");
        room.ReturnToLobby();
        yield return Until(() => SceneManager.GetActiveScene().name == MatchLaunchContext.LobbyScene, "Host returns to lobby");
        Check(!MatchLaunchContext.IsActive && !room.GameStarted && !room.IsHost, "Leave clears room and role");
        Cleanup();

        // Exercise the actual guest Update path against a fake TCP host.
        fakeHost = new TcpListener(IPAddress.Loopback, 0); fakeHost.Start();
        port = ((IPEndPoint)fakeHost.LocalEndpoint).Port;
        var accept = fakeHost.AcceptTcpClientAsync();
        Set(room, "joinInput", "127.0.0.1:" + port); Call(room, "JoinRoom");
        yield return Until(() => accept.IsCompleted, "Host accepts guest"); fakePeer = accept.GetAwaiter().GetResult();
        writer = new StreamWriter(fakePeer.GetStream()) { AutoFlush = true };
        reader = new StreamReader(fakePeer.GetStream());
        var name = reader.ReadLineAsync();
        yield return Until(() => name.IsCompleted, "Guest sends nickname");
        Check(name.GetAwaiter().GetResult().StartsWith("NAME "), "Guest nickname handshake");
        writer.WriteLine("MARCO/2 ROSTER 0:SG9zdA==|1:R3Vlc3Q=");
        yield return Until(() => room.MemberCount == 2, "Guest receives roster");
        Check(!room.CanStartGame && !room.IsHost, "Guest cannot start room");
        writer.WriteLine(RoomLaunchProtocol.StartCommand(1, new[] {
            new MatchMember(0, PrototypePlayerRole.Hunter, "Demon"),
            new MatchMember(1, PrototypePlayerRole.Survivor, "UnityChan"),
            new MatchMember(3, PrototypePlayerRole.Survivor, "Demon") }));
        yield return Until(() => SceneManager.GetActiveScene().name == MatchLaunchContext.GameScene, "Guest game scene transition");
        Check(MatchLaunchContext.LocalRole == PrototypePlayerRole.Survivor, "Guest uses server role");
        yield return VerifyLocalAssetOwner();
        ValidateGame();
        yield return VerifyGuestPose(room, writer, reader);
        yield return VerifyGuestRoleSwitch(room, writer, reader);
        writer.WriteLine(RoomLaunchProtocol.StartCommand(PrototypePlayerRole.Survivor));
        yield return null;
        fakePeer.Close(); fakePeer = null;
        yield return Until(() => SceneManager.GetActiveScene().name == MatchLaunchContext.LobbyScene, "Host disconnect returns guest to lobby");
        Check(!MatchLaunchContext.IsActive && !room.GameStarted && room.MemberCount == 0, "Guest disconnect clears role/session");
        Check(FindObjectsByType<RoomSessionPrototype>().Length == 1, "No duplicate persistent lobby sessions");
        Call(room, "CreateRoom"); room.StartGame();
        yield return Until(() => SceneManager.GetActiveScene().name == MatchLaunchContext.GameScene, "Solo host enters shared scene");
        yield return null;
        Check(MatchLaunchContext.Members.Length == 1 && FindObjectsByType<PlayerPoseReplicator>().Length == 1
            && FindObjectsByType<PrototypeTarget>().Length == 0, "Solo game creates only its real hunter participant, not a dummy survivor");
        Check(FindObjectsByType<Camera>().Length == 1, "Solo gameplay still has one camera");
        var soloBody = FindAnyObjectByType<FirstPersonBodyView>();
        yield return Until(() => soloBody.IsReady, "Solo original body ready for clipping regression");
        var soloCamera = Camera.main;
        soloBody.GetComponent<PrototypeFirstPersonController>().enabled = false;
        VerifyBodyTriangles(soloBody, soloCamera);
        foreach (float angle in new[] { 0f, 45f, 70f, 85f })
        {
            soloCamera.transform.localRotation = Quaternion.Euler(angle, 0, 0);
            yield return null;
            Capture(soloCamera, "Solo-Hunter-pitch-" + angle + "-masked.png");
            soloBody.GetComponent<CharacterVisualPrototype>().SetFirstPerson(false);
            Capture(soloCamera, "Solo-Hunter-pitch-" + angle + "-unmasked.png");
            soloBody.GetComponent<CharacterVisualPrototype>().SetFirstPerson(true);
        }
        // Reproduce the reported downward view during native actions and locomotion,
        // not only the role test's later walking pose.
        soloBody.GetComponent<HunterTagPrototype>().TrySwing();
        yield return new WaitForSeconds(.1f);
        Capture(soloCamera, "Solo-Hunter-down-attack.png");
        yield return new WaitForSeconds(.7f);
        for (int step = 0; step < 12; step++)
        {
            soloBody.transform.position += soloBody.transform.forward * .08f;
            yield return null;
        }
        Capture(soloCamera, "Solo-Hunter-down-walk.png");
        soloBody.GetComponent<CharacterMotionPresenter>().SetRemotePose(85, 7.2f, true);
        for (int step = 0; step < 8; step++) yield return null;
        Capture(soloCamera, "Solo-Hunter-down-run.png");
        yield return VerifySoloRoleSwitch(room);
        room.ReturnToLobby();
        yield return Until(() => SceneManager.GetActiveScene().name == MatchLaunchContext.LobbyScene, "Solo host returns to lobby");
        yield return VerifyFirstPersonBody();
        yield return VerifyAssetNameSwap();
    }

    private void VerifyRoleAssignmentProtocol()
    {
        var randomState = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(71239);
            foreach (var ids in new[] { new[] { 0 }, new[] { 0, 7 }, new[] { 0, 3, 9, 21 } })
            {
                var counts = new int[ids.Length];
                for (int trial = 0; trial < 8000; trial++)
                {
                    int hunterId = RoomSessionPrototype.PickRandomHunter(ids);
                    counts[Array.IndexOf(ids, hunterId)]++;
                    if (trial == 0) Check(ids.Count(id => RoomLaunchProtocol.RoleFor(id, hunterId) == PrototypePlayerRole.Hunter) == 1,
                        "Normal random assignment has one real player hunter, never one random coin per player");
                }
                Check(counts.All(count => Mathf.Abs(count - 8000f / ids.Length) < 350), "All participant IDs have equal hunter chance, including host and non-contiguous guest IDs");
            }
        }
        finally { UnityEngine.Random.state = randomState; }
        var soloSurvivor = new[] { new MatchMember(0, PrototypePlayerRole.Survivor, "UnityChan") };
        Check(RoomLaunchProtocol.TryParseDevelopmentRoles(RoomLaunchProtocol.DevelopmentRolesCommand(0, soloSurvivor), out _, out _),
            "Solo development role override may be survivor-only");
        Check(!RoomLaunchProtocol.TryParseMatch(RoomLaunchProtocol.StartCommand(0, soloSurvivor), out _, out _),
            "Normal game start cannot become a survivor-only match through a cheat message");
        Check(!RoomLaunchProtocol.TryParseMatch(RoomLaunchProtocol.DevelopmentRolesCommand(0, soloSurvivor), out _, out _),
            "Development command is not a normal START");
        Check(!RoomLaunchProtocol.TryParseDevelopmentRoles(RoomLaunchProtocol.DevelopmentRolesCommand(0, new[] {
            new MatchMember(0, PrototypePlayerRole.Hunter, "Demon"), new MatchMember(1, PrototypePlayerRole.Hunter, "Demon") }), out _, out _),
            "Development multiplayer assignment rejects multiple hunters");
        Check(!RoomLaunchProtocol.TryParseDevelopmentRoles(RoomLaunchProtocol.DevelopmentRolesCommand(0, new[] {
            new MatchMember(0, PrototypePlayerRole.Survivor, "UnityChan"), new MatchMember(1, PrototypePlayerRole.Survivor, "UnityChan") }), out _, out _),
            "Development multiplayer assignment rejects zero hunters");
    }

    private PlayerPoseReplicator CheckRoleOwner()
    {
        var actors = FindObjectsByType<PlayerPoseReplicator>();
        var local = Array.Find(actors, actor => actor.IsLocal);
        Check(local != null && actors.Count(actor => actor.IsLocal) == 1 && local.PlayerId == MatchLaunchContext.LocalPlayerId,
            "Role switch preserves exactly one local input owner and original player identity");
        Check(actors.Length == MatchLaunchContext.Members.Length, "Role switch creates no dummy participants or duplicate active bodies");
        Check(FindObjectsByType<Camera>().Length == 1 && FindObjectsByType<AudioListener>().Length == 1
            && FindObjectsByType<MicrophoneVisionController>().Length == 1, "Role switch keeps one camera/listener/microphone");
        Check(FindAnyObjectByType<PrototypeSceneBootstrap>().LocalRole == MatchLaunchContext.LocalRole, "Role title follows changed role");
        foreach (var actor in actors)
        {
            var member = Array.Find(MatchLaunchContext.Members, m => m.Id == actor.PlayerId);
            Check((actor.GetComponent<HunterTagPrototype>() != null) == (member.Role == PrototypePlayerRole.Hunter), "Every participant has the host-assigned role's action components");
            Check(actor.GetComponent<CharacterVisualPrototype>().AssetName == member.AssetName, "Every participant uses the changed registered asset");
            Check(actor.GetComponent<PrototypeFirstPersonController>().enabled == actor.IsLocal, "Only local participant has movement input");
        }
        bool hunter = MatchLaunchContext.LocalRole == PrototypePlayerRole.Hunter;
        Check(hunter ? local.GetComponent<SurvivorHealthHud>() == null
            : local.GetComponent<SurvivorHealthHud>().IsVisible && local.GetComponent<SurvivorInteractionPrototype>().enabled,
            "Changed role has the correct health HUD and interaction input");
        return local;
    }

    private static async Task<string> ReadCommand(StreamReader reader, string prefix)
    {
        for (int i = 0; i < 256; i++)
        {
            string line = await reader.ReadLineAsync();
            if (line == null) throw new IOException("Peer closed while waiting for " + prefix);
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
        }
        throw new IOException("Command not found: " + prefix);
    }

    private IEnumerator VerifyHostRoleSwitch(RoomSessionPrototype room, StreamWriter writer, StreamReader reader, StreamReader secondReader, int guestId, int secondId)
    {
        var oldLocal = Array.Find(FindObjectsByType<PlayerPoseReplicator>(), actor => actor.IsLocal);
        var oldRole = MatchLaunchContext.LocalRole; var position = oldLocal.transform.position;
        uint sequence = oldLocal.PublishedSequence;
        Camera.main.GetComponent<MicrophoneVisionController>().SetDevelopmentFullVision(true);
        Check(room.RequestDevelopmentRoleSwitch(), "Host invokes the same F10 development role switch path");
        Check(!room.RequestDevelopmentRoleSwitch(), "Repeated role key is debounced");
        yield return null;
        var local = CheckRoleOwner();
        yield return Until(() => local.GetComponent<FirstPersonBodyView>().IsReady, "Changed host original asset body ready");
        Check(MatchLaunchContext.LocalRole != oldRole && Vector3.Distance(local.transform.position, position) < .15f,
            "Host changes own role in place without restarting the scene or teleporting");
        Check(Camera.main.GetComponent<MicrophoneVisionController>().DevelopmentFullVision, "Role switch preserves F8 full vision");
        Check(MatchLaunchContext.Members.Count(m => m.Role == PrototypePlayerRole.Hunter) == 1, "Multiplayer role exchange keeps one hunter");
        var first = ReadCommand(reader, "MARCO/3 DEV_ROLES "); var second = ReadCommand(secondReader, "MARCO/3 DEV_ROLES ");
        yield return Until(() => first.IsCompleted && second.IsCompleted, "Host broadcasts changed roles to all sockets");
        Check(RoomLaunchProtocol.TryParseDevelopmentRoles(first.Result, out int id, out var changed) && id == guestId
            && RoomLaunchProtocol.TryParseDevelopmentRoles(second.Result, out int otherId, out _) && otherId == secondId,
            "Role broadcast preserves each recipient's authoritative player ID");
        Check(changed.Count(m => m.Role == PrototypePlayerRole.Hunter) == 1, "Guests receive the same one-hunter assignment");
        var nextPose = ReadCommand(reader, "MARCO/3 POSE ");
        yield return Until(() => nextPose.IsCompleted, "Host continues publishing pose after role change");
        Check(PlayerPoseProtocol.TryParse(nextPose.Result, out var pose) && pose.PlayerId == 0 && pose.Sequence > sequence,
            "Changed host pose sequence remains monotonic");
        Capture(Camera.main, "Development-host-role-switch.png");
        yield return new WaitForSeconds(.55f);
        var guestRole = Array.Find(MatchLaunchContext.Members, m => m.Id == guestId).Role;
        writer.WriteLine(RoomLaunchProtocol.DevelopmentRoleRequest + " 0");
        yield return new WaitForSeconds(.1f);
        Check(Array.Find(MatchLaunchContext.Members, m => m.Id == guestId).Role == guestRole, "Forged role request payload cannot choose another player identity");
        writer.WriteLine(RoomLaunchProtocol.DevelopmentRoleRequest);
        yield return Until(() => Array.Find(MatchLaunchContext.Members, m => m.Id == guestId).Role != guestRole, "Host accepts guest role request using its socket identity");
        yield return null;
        CheckRoleOwner();
        first = ReadCommand(reader, "MARCO/3 DEV_ROLES "); second = ReadCommand(secondReader, "MARCO/3 DEV_ROLES ");
        yield return Until(() => first.IsCompleted && second.IsCompleted, "Guest-requested role exchange reaches both guest sockets");
        writer.WriteLine(PlayerPoseProtocol.Encode(new PlayerPoseState { PlayerId = guestId, Sequence = 3,
            X = 2, Y = 1, Z = 4, Pitch = 30, Yaw = 35, Attack = 2, Interaction = 2 }));
        yield return Until(() => room.TryGetPose(guestId, out var accepted) && accepted.Sequence == 3, "Guest pose continues after role replacement");
        var guest = Array.Find(FindObjectsByType<PlayerPoseReplicator>(), actor => actor.PlayerId == guestId);
        Check(guest.ReceivedSequence == 3, "Replacement remote original body receives the continued TCP pose stream");
    }

    private IEnumerator VerifyGuestRoleSwitch(RoomSessionPrototype room, StreamWriter writer, StreamReader reader)
    {
        var changed = new[] { new MatchMember(0, PrototypePlayerRole.Survivor, "UnityChan"), new MatchMember(1, PrototypePlayerRole.Hunter, "Demon") };
        writer.WriteLine(RoomLaunchProtocol.DevelopmentRolesCommand(0, changed));
        yield return new WaitForSeconds(.1f);
        Check(MatchLaunchContext.LocalRole == PrototypePlayerRole.Survivor && MatchLaunchContext.LocalPlayerId == 1,
            "Guest ignores a role broadcast addressed to another local player ID");
        writer.WriteLine(RoomLaunchProtocol.DevelopmentRolesCommand(1, changed));
        yield return Until(() => MatchLaunchContext.LocalRole == PrototypePlayerRole.Hunter, "Guest applies host-authoritative role exchange");
        yield return null;
        var local = CheckRoleOwner();
        yield return Until(() => local.GetComponent<FirstPersonBodyView>().IsReady, "Changed guest original hunter body ready");
        uint sequence = local.PublishedSequence;
        Check(room.RequestDevelopmentRoleSwitch(), "Guest F10 sends a request rather than assigning itself an authoritative role");
        var request = ReadCommand(reader, RoomLaunchProtocol.DevelopmentRoleRequest);
        yield return Until(() => request.IsCompleted, "Guest role request arrives on actual TCP socket");
        Check(MatchLaunchContext.LocalRole == PrototypePlayerRole.Hunter, "Guest waits for host approval before changing role");
        changed = new[] { new MatchMember(0, PrototypePlayerRole.Hunter, "Demon"), new MatchMember(1, PrototypePlayerRole.Survivor, "UnityChan") };
        writer.WriteLine(RoomLaunchProtocol.DevelopmentRolesCommand(1, changed));
        yield return Until(() => MatchLaunchContext.LocalRole == PrototypePlayerRole.Survivor, "Guest switches back after host role response");
        yield return null; local = CheckRoleOwner();
        yield return Until(() => local.GetComponent<FirstPersonBodyView>().IsReady, "Changed guest survivor body ready");
        Check(local.PublishedSequence >= sequence, "Guest role change preserves local outgoing pose counters");
        Capture(Camera.main, "Development-guest-role-switch.png");
    }

    private IEnumerator VerifySoloRoleSwitch(RoomSessionPrototype room)
    {
        var old = Array.Find(FindObjectsByType<PlayerPoseReplicator>(), actor => actor.IsLocal);
        uint sequence = old.PublishedSequence;
        Camera.main.GetComponent<MicrophoneVisionController>().SetDevelopmentFullVision(true);
        for (int toggle = 0; toggle < 4; toggle++)
        {
            var priorRole = MatchLaunchContext.LocalRole;
            yield return new WaitForSeconds(.55f);
            Check(room.RequestDevelopmentRoleSwitch(), "Solo F10 works even when no opposite-role participant exists");
            yield return null;
            var local = CheckRoleOwner();
            yield return Until(() => local.GetComponent<FirstPersonBodyView>().IsReady, "Solo changed asset body initialized");
            Check(MatchLaunchContext.LocalRole != priorRole && MatchLaunchContext.LocalPlayerId == 0 && room.MemberCount == 1,
                "Solo role toggles without inventing another player or changing identity");
            Check(local.PublishedSequence >= sequence, "Repeated solo role changes retain monotonic pose sequence");
            sequence = local.PublishedSequence;
            Check(Camera.main.GetComponent<MicrophoneVisionController>().DevelopmentFullVision, "Repeated role changes retain development full vision");
            Check(Mathf.Abs(Mathf.DeltaAngle(0, Camera.main.transform.localEulerAngles.x) - 85) < .1f,
                "Role changes preserve downward camera look direction");
            if (MatchLaunchContext.LocalRole == PrototypePlayerRole.Survivor)
            {
                Check(local.GetComponent<SurvivorHealthHud>().FillAmount == 1, "Fresh survivor test role has full temporary health bar");
                local.GetComponent<SurvivorInteractionPrototype>().TryInteract();
                Check(local.GetComponent<FirstPersonBodyView>().IsActing, "Changed survivor can interact with its real asset arms");
            }
            else Check(local.GetComponent<HunterTagPrototype>().TrySwing(), "Changed hunter can attack");
            Capture(Camera.main, "Development-solo-role-" + toggle + ".png");
        }
    }

    private IEnumerator VerifyLocalAssetOwner()
    {
        var hunter = GameObject.Find("Hunter Player").GetComponent<FirstPersonBodyView>();
        var survivor = GameObject.Find("Survivor Player").GetComponent<FirstPersonBodyView>();
        var local = MatchLaunchContext.LocalRole == PrototypePlayerRole.Hunter ? hunter : survivor;
        var remote = local == hunter ? survivor : hunter;
        yield return Until(() => local.IsReady, "Assigned role's asset view is ready in combined scene");
        Check(local.IsVisible, "Combined scene renders own asset limbs");
        Check(!remote.enabled && !remote.IsReady, "Other role creates no first-person asset rig");
    }

    private void ValidateGame()
    {
        var hud = FindAnyObjectByType<PrototypeSceneBootstrap>();
        Check(hud.LocalRole == MatchLaunchContext.LocalRole, "Shared scene applies assigned role");
        Check(FindObjectsByType<Camera>().Length == 1, "One gameplay camera");
        Check(FindObjectsByType<AudioListener>().Length == 1, "One audio listener");
        Check(FindObjectsByType<MicrophoneVisionController>().Length == 1, "One microphone vision mask");
        Check(FindObjectsByType<PrototypeTarget>().Length == Array.FindAll(MatchLaunchContext.Members, m => m.Role == PrototypePlayerRole.Survivor).Length,
            "Exactly one body per actual participant, no training dummy");
        Check(FindObjectsByType<PlayerPoseReplicator>().Length == MatchLaunchContext.Members.Length, "Network participant count matches assignment");
        var survivor = GameObject.Find("Survivor Player");
        var healthHud = survivor.GetComponent<SurvivorHealthHud>();
        var target = survivor.GetComponent<PrototypeTarget>();
        Check(healthHud.enabled == (hud.LocalRole == PrototypePlayerRole.Survivor), "Health HUD ownership");
        Check(healthHud.FillAmount == 1, "Full health bar");
        target.Tag(); Check(healthHud.FillAmount == 0.5f, "First hit halves bar");
        target.Health.Bandage(); Check(healthHud.FillAmount == 0.75f, "Bandage restores half a life");
        target.Treat(); Check(healthHud.FillAmount == 1, "Treatment restores bar");
        target.Tag(); target.Tag(); Check(!healthHud.IsVisible && healthHud.FillAmount == 0, "Ghost hides health bar");
        target.ResetHealth();
    }

    private IEnumerator VerifyHostPose(RoomSessionPrototype room, StreamWriter writer, StreamReader reader, int guestId)
    {
        var localRead = reader.ReadLineAsync();
        yield return Until(() => localRead.IsCompleted, "Host sends local pose over real TCP");
        Check(PlayerPoseProtocol.TryParse(localRead.Result, out var sent) && sent.PlayerId == 0, "Host pose includes authoritative local identity");
        var remote = Array.Find(FindObjectsByType<PlayerPoseReplicator>(), p => p.PlayerId == guestId);
        Check(remote != null && !remote.IsLocal, "Host has guest-controlled world body");
        var head = remote.GetComponent<CharacterVisualPrototype>().ResolveBone(HumanBodyBones.Head);
        var pose = new PlayerPoseState { PlayerId = guestId, Sequence = 1, X = 2, Y = 1, Z = 4, Pitch = 70, Yaw = 35, Speed = 7, Running = true };
        writer.WriteLine(PlayerPoseProtocol.Encode(pose));
        yield return Until(() => remote.ReceivedSequence == 1, "Host accepts guest pose on the socket");
        for (int i = 0; i < 5; i++) yield return null;
        Quaternion down = head.rotation;
        Check(Vector3.Distance(remote.transform.position, new Vector3(2, 1, 4)) < .2f, "Host displays guest position");
        Check(remote.GetComponent<CharacterMotionPresenter>().Running, "Host displays guest running");
        pose.Sequence = 2; pose.Pitch = -60; pose.Attack = 1; pose.Interaction = 1;
        writer.WriteLine(PlayerPoseProtocol.Encode(pose));
        yield return Until(() => remote.ReceivedSequence == 2, "Host receives guest action and gaze change");
        yield return new WaitForSeconds(.15f);
        Check(Quaternion.Angle(down, head.rotation) > 30, "TCP guest gaze rotates the original visible head");
        Check(remote.GetComponent<CharacterMotionPresenter>().IsInteracting, "TCP action changes remote original body");
        pose.Sequence = 2; pose.X = 900; writer.WriteLine(PlayerPoseProtocol.Encode(pose));
        pose.Sequence = 3; pose.PlayerId = 0; writer.WriteLine(PlayerPoseProtocol.Encode(pose));
        writer.WriteLine("MARCO/3 POSE 1 3 NaN 1 1 0 0 0 0 0 0 0");
        yield return new WaitForSeconds(.15f);
        Check(room.TryGetPose(guestId, out var accepted) && accepted.Sequence == 2 && accepted.X == 2,
            "Host rejects duplicate sequence, forged identity and non-finite coordinates");
    }

    private IEnumerator VerifyGuestPose(RoomSessionPrototype room, StreamWriter writer, StreamReader reader)
    {
        Check(MatchLaunchContext.LocalPlayerId == 1, "Guest keeps server-assigned player ID");
        var extra = Array.Find(FindObjectsByType<PlayerPoseReplicator>(), p => p.PlayerId == 3);
        Check(extra != null && extra.GetComponent<CharacterVisualPrototype>().AssetName == "Demon", "Third participant spawns using its registered asset name");
        var localRead = reader.ReadLineAsync();
        yield return Until(() => localRead.IsCompleted, "Guest sends pose over real TCP");
        Check(PlayerPoseProtocol.TryParse(localRead.Result, out var sent) && sent.PlayerId == 1, "Guest pose uses its assigned identity");
        writer.WriteLine(PlayerPoseProtocol.Encode(new PlayerPoseState {
            PlayerId = 3, Sequence = 1, X = -3, Y = 1, Z = 4, Pitch = 65, Yaw = -25,
            Speed = 6, Running = true, Interaction = 1, InteractionSucceeded = true }));
        yield return Until(() => extra.ReceivedSequence == 1, "Guest receives another survivor pose");
        for (int i = 0; i < 5; i++) yield return null;
        Check(Vector3.Distance(extra.transform.position, new Vector3(-3, 1, 4)) < .2f && extra.GetComponent<CharacterMotionPresenter>().Running,
            "Guest displays other participant movement and running");
        Check(extra.GetComponent<CharacterMotionPresenter>().IsInteracting, "Guest plays remote interaction without running treatment logic");
        writer.WriteLine("MARCO/3 LEFT 3");
        yield return Until(() => extra == null, "Guest removes a disconnected participant body");
    }

    private IEnumerator VerifyFirstPersonBody()
    {
        foreach (string scene in new[] { "Prototype_HunterTest", "Prototype_SurvivorTest" })
        {
            SceneManager.LoadSceneAsync(scene);
            yield return Until(() => SceneManager.GetActiveScene().name == scene, "Role test scene loaded");
            yield return null;
            var body = FindAnyObjectByType<FirstPersonBodyView>();
            yield return Until(() => body != null && body.IsReady, "First-person rig ready");
            Check(body.IsVisible, "First-person limbs visible");
            Check(FindObjectsByType<Camera>().Length == 1, "Viewmodel adds no camera");
            var camera = Camera.main;
            var assetVisual = body.GetComponent<CharacterVisualPrototype>();
            var originalSkins = body.CharacterVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Check(body.SourceRendererCount == assetVisual.Definition.visualPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length,
                "Renderer count exactly matches original prefab: no extra arm/leg renderers");
            Check(camera.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0 && camera.GetComponentsInChildren<Transform>(true).Length == 1,
                "Camera contains no copied limb meshes or skeleton");
            Check(body.CharacterVisual.GetComponentsInChildren<Transform>(true).Length == assetVisual.Definition.visualPrefab.GetComponentsInChildren<Transform>(true).Length + 1,
                "Body hierarchy contains exactly the original prefab skeleton and one asset anchor");
            Check(body.RightHand == assetVisual.ResolveBone(HumanBodyBones.RightHand)
                && body.LeftHand == assetVisual.ResolveBone(HumanBodyBones.LeftHand), "Own hands ARE the original body's hands");
            foreach (var skin in originalSkins)
            {
                Check((camera.cullingMask & (1 << skin.gameObject.layer)) != 0, "Owner camera renders original body layer");
            }
            Check(body.HeadTrianglesRemoved > 0, "Head/hair are hidden only during own-camera rendering");
            var sourceMeshes = Array.ConvertAll(originalSkins, skin => skin.sharedMesh);
            var rigidHead = Array.FindAll(body.CharacterVisual.GetComponentsInChildren<MeshRenderer>(true), part =>
                part.transform.IsChildOf(assetVisual.ResolveBone(HumanBodyBones.Head))
                || part.transform.IsChildOf(assetVisual.ResolveBone(HumanBodyBones.Neck)));
            var rigidEnabled = Array.ConvertAll(rigidHead, part => part.enabled);
            bool maskedDuringDraw = false;
            bool rigidHiddenDuringDraw = false;
            Camera.CameraCallback checkDraw = drawn =>
            {
                if (drawn != camera) return;
                maskedDuringDraw = body.IsMaskApplied;
                rigidHiddenDuringDraw = Array.TrueForAll(rigidHead, part => !part.enabled);
            };
            Camera.onPreRender += checkDraw;
            try { Capture(camera, scene + "-initial.png"); }
            finally { Camera.onPreRender -= checkDraw; }
            Check(maskedDuringDraw && !body.IsMaskApplied, "Own render applies head visibility and restores it immediately afterward");
            Check(rigidHiddenDuringDraw, "Unskinned head/back-face pieces are also hidden only for owner camera");
            for (int i = 0; i < rigidHead.Length; i++) Check(rigidHead[i].enabled == rigidEnabled[i], "Rigid head part is restored for Scene and other cameras");
            for (int i = 0; i < originalSkins.Length; i++)
                Check(originalSkins[i].sharedMesh == sourceMeshes[i], "Scene/other cameras keep full original mesh after own render");
            VerifyBodyTriangles(body, camera);
            Vector3 restingHand = camera.transform.InverseTransformPoint(body.RightHand.position);
            var hunter = body.GetComponent<HunterTagPrototype>();
            if (hunter != null)
            {
                Check(hunter.TrySwing() && body.IsActing, "Accepted attack starts arm action");
                Check(!hunter.TrySwing(), "Attack cooldown rejects duplicate swing");
            }
            else
            {
                body.GetComponent<SurvivorInteractionPrototype>().TryInteract();
                Check(body.IsActing, "E interaction intent starts hand reach without a target");
            }
            yield return new WaitForSeconds(0.1f);
            Check(Vector3.Distance(camera.transform.InverseTransformPoint(body.RightHand.position), restingHand) > 0.02f, "Action changes asset hand pose");
            Capture(camera, scene + "-action.png");
            yield return new WaitForSeconds(0.7f);
            Check(!body.IsActing, "Action returns to idle");
            Capture(camera, scene + "-idle.png");
            var movement = body.GetComponent<PrototypeFirstPersonController>();
            movement.enabled = false;
            Transform foot = null;
            foot = body.GetComponent<CharacterVisualPrototype>().ResolveBone(HumanBodyBones.RightFoot);
            Check(foot != null, "Asset foot joint is present");
            Vector3 restingFoot = body.transform.InverseTransformPoint(foot.position);
            for (int step = 0; step < 12; step++)
            {
                body.transform.position += body.transform.forward * 0.08f;
                yield return null;
            }
            Check(Vector3.Distance(body.transform.InverseTransformPoint(foot.position), restingFoot) > 0.01f, "Existing walk clip moves asset foot joint");
            Capture(camera, scene + "-walk.png");
            camera.transform.localRotation = Quaternion.Euler(85, 0, 0);
            body.transform.position += body.transform.forward * 0.2f;
            yield return null;
            Capture(camera, scene + "-legs.png");
            var motion = body.GetComponent<CharacterMotionPresenter>();
            Check(motion != null && motion.IsReady, "Registered asset has shared world/own-view animation");
            var head = body.GetComponent<CharacterVisualPrototype>().ResolveBone(HumanBodyBones.Head);
            Check(head != null, "Asset head mapping is present");
            motion.SetRemotePose(70, 0, false);
            yield return null;
            Quaternion loweredHead = head.rotation;
            motion.SetRemotePose(-60, 0, false);
            yield return null;
            Check(Quaternion.Angle(loweredHead, head.rotation) > 45, "World asset head visibly follows up/down gaze");
            motion.SetRemotePose(0, 7.2f, true);
            for (int step = 0; step < 8; step++) yield return null;
            Check(motion.Running && motion.Speed > 7, "Run state drives the same asset skeleton");
            var visual = body.GetComponent<CharacterVisualPrototype>();
            var hand = visual.ResolveBone(HumanBodyBones.RightHand);
            motion.SetRemotePose(0, 0, false);
            yield return null;
            Vector3 nativeHand = body.transform.InverseTransformPoint(hand.position);
            motion.PlayInteraction(true);
            yield return new WaitForSeconds(.2f);
            Check(Vector3.Distance(nativeHand, body.transform.InverseTransformPoint(hand.position)) > .02f,
                "Interaction changes the externally visible asset hand, not just own-view hands");
            Vector3 cameraPosition = camera.transform.localPosition;
            Quaternion cameraRotation = camera.transform.localRotation;
            visual.SetFirstPerson(false);
            camera.transform.position = body.transform.position - body.transform.forward * 4 + Vector3.up * 1.5f;
            camera.transform.LookAt(body.transform.position);
            Capture(camera, scene + "-world-body.png");
            camera.transform.localPosition = cameraPosition; camera.transform.localRotation = cameraRotation;
            visual.SetFirstPerson(true);
            var target = body.GetComponent<PrototypeTarget>();
            if (target != null)
            {
                target.Tag(); target.Tag();
                yield return null;
                Check(!body.IsVisible && !body.GetComponent<SurvivorHealthHud>().IsVisible, "Ghost hides body and health HUD");
                target.ResetHealth(); yield return null;
                Check(body.IsVisible && body.GetComponent<SurvivorHealthHud>().IsVisible, "Reset restores body and health HUD");
            }
        }
    }

    private void VerifyBodyTriangles(FirstPersonBodyView body, Camera camera)
    {
        var visual = body.GetComponent<CharacterVisualPrototype>();
        var head = visual.ResolveBone(HumanBodyBones.Head);
        var neck = visual.ResolveBone(HumanBodyBones.Neck);
        var skins = body.CharacterVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var originals = Array.ConvertAll(skins, skin => skin.sharedMesh);
        bool sawMask = false, geometryUnchanged = true, bodyPreserved = true;
        int protectedTriangles = 0;
        Camera.CameraCallback inspect = drawn =>
        {
            if (drawn != camera) return;
            sawMask = body.IsMaskApplied;
            for (int s = 0; s < skins.Length; s++)
            {
                var original = originals[s]; var masked = skins[s].sharedMesh;
                geometryUnchanged &= original.vertices.SequenceEqual(masked.vertices)
                    && original.normals.SequenceEqual(masked.normals) && original.tangents.SequenceEqual(masked.tangents)
                    && original.uv.SequenceEqual(masked.uv) && original.bindposes.SequenceEqual(masked.bindposes)
                    && original.boneWeights.SequenceEqual(masked.boneWeights) && original.blendShapeCount == masked.blendShapeCount;
                var weights = original.boneWeights;
                if (weights.Length == 0) continue; // Dedicated unweighted facial parts may be hidden entirely.
                var headBones = Array.ConvertAll(skins[s].bones, bone => bone != null
                    && (bone.IsChildOf(head) || (neck != null && bone.IsChildOf(neck))));
                float HeadWeight(int vertex)
                {
                    var w = weights[vertex];
                    float Part(int index, float amount) => index < headBones.Length && headBones[index] ? amount : 0;
                    return Part(w.boneIndex0, w.weight0) + Part(w.boneIndex1, w.weight1)
                        + Part(w.boneIndex2, w.weight2) + Part(w.boneIndex3, w.weight3);
                }
                for (int sub = 0; sub < original.subMeshCount; sub++)
                {
                    var retained = new HashSet<(int, int, int)>();
                    var indices = masked.GetTriangles(sub);
                    for (int t = 0; t < indices.Length; t += 3) retained.Add((indices[t], indices[t + 1], indices[t + 2]));
                    var source = original.GetTriangles(sub);
                    for (int t = 0; t < source.Length; t += 3)
                    {
                        if (HeadWeight(source[t]) >= .5f || HeadWeight(source[t + 1]) >= .5f || HeadWeight(source[t + 2]) >= .5f) continue;
                        protectedTriangles++;
                        bodyPreserved &= retained.Contains((source[t], source[t + 1], source[t + 2]));
                    }
                }
            }
        };
        Camera.onPreRender += inspect;
        try { Capture(camera, SceneManager.GetActiveScene().name + "-body-mesh-regression.png"); }
        finally { Camera.onPreRender -= inspect; }
        Check(sawMask, "Regression inspects the actual own-camera visibility mesh during render");
        Check(geometryUnchanged, "Head masking preserves all original vertices, skin weights, bind poses and blend shapes");
        Check(protectedTriangles > 1000 && bodyPreserved, "All non-head body/arm/leg triangles survive, including imported scaled renderers");
        Check(Array.TrueForAll(Enumerable.Range(0, skins.Length).ToArray(), i => skins[i].sharedMesh == originals[i]),
            "Regression restores the full original renderer meshes for other cameras");
        Debug.Log("Body clipping regression: preserved " + protectedTriangles + " non-head triangles on " + visual.AssetName);
    }

    private static void Capture(Camera camera, string path)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        // Unity removes Temp on shutdown; preserve QA images outside that directory.
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PrototypeVerificationScreenshots"));
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, path);
        var target = RenderTexture.GetTemporary(960, 600, 24);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var image = new Texture2D(960, 600, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target); Destroy(image);
        }
    }

    private IEnumerator VerifyAssetNameSwap()
    {
        Check(CharacterAssetDefinition.Find("Demon") != null && CharacterAssetDefinition.Find("UnityChan") != null, "Both registered character definitions load by name");
        Check(CharacterAssetDefinition.Find("../Demon") == null, "Asset names cannot escape the registered catalog");
        var original = FindAnyObjectByType<FirstPersonBodyView>();
        var inactiveParent = new GameObject("Asset name swap verification"); inactiveParent.SetActive(false);
        var replacement = Instantiate(original.gameObject, inactiveParent.transform);
        var visual = replacement.GetComponent<CharacterVisualPrototype>();
        Set(visual, "characterAssetName", "Demon");
        replacement.GetComponent<PrototypeFirstPersonController>().enabled = false;
        replacement.GetComponent<SurvivorHealthHud>().enabled = false;
        var camera = replacement.GetComponentInChildren<Camera>(true);
        camera.transform.localRotation = Quaternion.identity;
        original.gameObject.SetActive(false); inactiveParent.SetActive(true);
        var body = replacement.GetComponent<FirstPersonBodyView>();
        yield return Until(() => body.IsReady, "Changing only registered name builds the replacement first-person body");
        Check(visual.Definition.name == "Demon" && body.CharacterVisual == visual.BodyRoot, "Body and own view use the same replacement asset");
        Check(visual.ResolveBone(HumanBodyBones.RightHand) != null, "Generic mapping resolves after replacing a Humanoid asset");
        Check(body.RightHand == visual.ResolveBone(HumanBodyBones.RightHand) && camera.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0,
            "Name-only replacement still uses only the original body, never additional arms");
        replacement.GetComponent<SurvivorInteractionPrototype>().TryInteract();
        yield return new WaitForSeconds(0.1f);
        Check(body.IsActing && body.IsVisible, "Role behavior survives the asset-name change");
        Capture(camera, "Character-asset-name-swap.png");
        Destroy(inactiveParent);
    }

    private IEnumerator Until(Func<bool> condition, string message)
    {
        float deadline = Time.realtimeSinceStartup + 15;
        while (!condition()) { if (Time.realtimeSinceStartup > deadline) throw new Exception("Timeout: " + message); yield return null; }
    }

    private void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private void Cleanup() { fakePeer?.Close(); fakePeer = null; secondPeer?.Close(); secondPeer = null; fakeHost?.Stop(); fakeHost = null; }
    private void OnDestroy() { Cleanup(); }
}
