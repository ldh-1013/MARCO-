using System;
using System.IO;
using FishNet.Transporting;
using Steamworks;
using UnityEngine;

namespace Marco.Network.Lobby
{
    // Steam friends-only lobby + invite, ported from RoomSessionPrototype. The Steam lobby only finds
    // the host; the game session itself runs over FishNet (LanConnector.HostSteam / JoinSteam).
    // Guests join by accepting an invite or through the "+connect_lobby <id>" launch argument.
    [RequireComponent(typeof(LanConnector))]
    public sealed class SteamLobby : MonoBehaviour
    {
        private const int MaxMembers = 16;

        private LanConnector connector;
        private bool createLobbyPending;
        private CSteamID lobbyId;
        private Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        private CallResult<LobbyCreated_t> lobbyCreated;
        private CallResult<LobbyEnter_t> lobbyEntered;

        public bool Ready { get; private set; }
        public bool InLobby { get; private set; }
        // Joinable only after the FishNet server is up, so invites never lead to a dead host.
        public bool LobbyOpen { get; private set; }
        public string Status { get; private set; } = "Steam 릴레이 연결 대기 중";
        public ulong SteamId => Ready ? SteamUser.GetSteamID().m_SteamID : 0;
        public bool IsLobbyOwner => InLobby && SteamMatchmaking.GetLobbyOwner(lobbyId) == SteamUser.GetSteamID();
        public bool CanCreate => Ready && !InLobby && !createLobbyPending;

        // Lobby name shown to friends: "<nickname>의 방".
        public string Nickname { get; set; } = "Player";

        private void Awake()
        {
            connector = GetComponent<LanConnector>();
            connector.ServerStateChanged += OnServerStateChanged;
        }

        private void Start()
        {
            string appIdPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");
#if UNITY_EDITOR
            // Spacewar (480) is only a local development App ID. Replace it with the game's App ID before release.
            if (!File.Exists(appIdPath)) File.WriteAllText(appIdPath, "480");
#endif
            try
            {
                var result = SteamAPI.InitEx(out string steamError);
                Ready = result == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
                if (!Ready)
                {
                    string appId = File.Exists(appIdPath) ? File.ReadAllText(appIdPath).Trim() : "없음";
                    string reason = string.IsNullOrWhiteSpace(steamError) ? "상세 원인 없음" : steamError.Trim();
                    Status = "Steam API 초기화 실패 (" + result + "): " + reason;
                    Debug.LogError("[Steam] API init failed. Result=" + result + ", AppId=" + appId + ", Details=" + reason);
                    return;
                }
            }
            catch (Exception e)
            {
                Status = "Steam API 초기화 예외: " + e.Message;
                Debug.LogException(e);
                return;
            }

            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            uint appIdNow = SteamUtils.GetAppID().m_AppId;
            Status = appIdNow == 480
                ? "App ID 480 (Spacewar) 테스트 중 · 실제 게임 초대에는 게임 App ID가 필요합니다."
                : "Steam 릴레이 준비됨 · App ID " + appIdNow + " · SteamID " + SteamUser.GetSteamID().m_SteamID;
            JoinFromLaunchArguments();
        }

        private void Update()
        {
            if (Ready) SteamAPI.RunCallbacks();
        }

        private void OnDestroy()
        {
            if (connector != null) connector.ServerStateChanged -= OnServerStateChanged;
            LeaveLobby();
            if (Ready) SteamAPI.Shutdown();
            Ready = false;
        }

        public void CreateLobby()
        {
            if (!Ready) { Status = "Steam 준비가 안 됐습니다. Steam 실행 상태를 확인하세요."; return; }
            if (!CanCreate || connector.Status == LanConnectionStatus.Connecting) return;
            createLobbyPending = true;
            Status = "Steam 친구 전용 로비 생성 중…";
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxMembers));
        }

        public void InviteFriends()
        {
            if (!Ready || !LobbyOpen || !IsLobbyOwner)
            {
                Status = "호스트 FishNet 서버가 준비된 뒤에 초대할 수 있습니다.";
                return;
            }
            SteamFriends.ActivateGameOverlayInviteDialog(lobbyId);
        }

        // "Steam 연결 종료": leaves both the FishNet session and the Steam lobby.
        public void Stop()
        {
            connector.Leave();
            LeaveLobby();
            Status = "Steam 릴레이 연결을 종료했습니다.";
        }

        public void LeaveLobby()
        {
            if (InLobby && Ready) SteamMatchmaking.LeaveLobby(lobbyId);
            InLobby = false;
            LobbyOpen = false;
            createLobbyPending = false;
            lobbyId = CSteamID.Nil;
        }

        private void JoinFromLaunchArguments()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
            {
                if (arguments[i] != "+connect_lobby" || !ulong.TryParse(arguments[i + 1], out ulong id)) continue;
                Status = "Steam 초대 로비에 참가 중…";
                lobbyEntered.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));
                return;
            }
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            createLobbyPending = false;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                Status = "Steam 로비 생성 실패: " + result.m_eResult;
                return;
            }
            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            InLobby = true;
            SteamMatchmaking.SetLobbyData(lobbyId, "name", Nickname + "의 방");
            SteamMatchmaking.SetLobbyData(lobbyId, "host", SteamUser.GetSteamID().m_SteamID.ToString());
            LobbyOpen = false;
            SteamMatchmaking.SetLobbyJoinable(lobbyId, false);
            Status = "Steam 로비 생성됨 · FishNet 서버 시작 중…";
            StartHost();
        }

        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t request)
        {
            if (!Ready) return;
            Status = "Steam 초대를 수락했습니다. 로비에 참가 중…";
            lobbyEntered.Set(SteamMatchmaking.JoinLobby(request.m_steamIDLobby));
        }

        private void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            if (ioFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Status = "Steam 로비 참가에 실패했습니다.";
                return;
            }
            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            InLobby = true;
            LobbyOpen = false;
            var owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
            if (owner == SteamUser.GetSteamID())
            {
                if (!connector.IsHost) StartHost();
                return;
            }
            Status = "Steam 릴레이로 호스트에 연결 중…";
            if (!connector.JoinSteam(owner.m_SteamID)) Status = "FishNet 클라이언트 시작 요청이 거부됐습니다.";
        }

        private void StartHost()
        {
            Status = "Steam 릴레이 호스트 시작 중…";
            if (!connector.HostSteam()) Status = "FishNet Steam 호스트 시작 요청이 거부됐습니다.";
        }

        private void OnServerStateChanged(ServerConnectionStateArgs args)
        {
            if (!InLobby || !connector.UsingSteam) return;
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                LobbyOpen = SteamMatchmaking.SetLobbyJoinable(lobbyId, true);
                Status = LobbyOpen
                    ? "Steam 로비 생성됨 · 친구를 초대하세요."
                    : "FishNet 서버는 시작됐지만 Steam 로비를 초대 가능 상태로 열지 못했습니다.";
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                Status = "FishNet Steam 호스트가 종료됐습니다.";
                LeaveLobby();
            }
        }
    }
}
