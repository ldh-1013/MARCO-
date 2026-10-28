using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishNet.Transporting;
using FishNet.Transporting.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using LobbyModel = Unity.Services.Lobbies.Models.Lobby;

namespace Marco.Network.Lobby
{
    /// <summary>
    /// Owns the Unity Lobby directory and the Unity Relay allocation used by a FishNet room.
    /// The displayed room code is the Lobby code; the private Relay join code is kept in member data.
    /// </summary>
    [RequireComponent(typeof(LanConnector))]
    public class UnityRelayLobby : MonoBehaviour
    {
        private const int MaxPlayers = 6;
        private const float LobbyHeartbeatSeconds = 15f;
        private const string RelayCodeDataKey = "relayJoinCode";

        private LanConnector connector;
        private FishyUnityTransport relayTransport;
        private Task initializationTask;
        private LobbyModel currentLobby;
        private List<LobbyModel> availableRooms = new();
        private bool creatingRoom;
        private bool publishLobbyWhenServerStarts;
        private bool stopping;
        private bool leavingLobby;
        private float nextHeartbeatTime;
        private string pendingRelayJoinCode;
        private string pendingLobbyName;

        public bool Ready { get; private set; }
        public bool InLobby => currentLobby != null;
        public bool LobbyOpen { get; private set; }
        public bool IsLobbyOwner { get; private set; }
        public bool IsRefreshingRooms { get; private set; }
        public string Status { get; private set; } = "Unity 서비스 연결 대기 중";
        public string RoomListStatus { get; private set; } = "Join을 눌러 방 목록을 불러오세요.";
        public string CurrentLobbyCode => currentLobby?.LobbyCode ?? string.Empty;
        public IReadOnlyList<LobbyModel> AvailableRooms => availableRooms;
        public bool CanCreate => Ready && !InLobby && !creatingRoom;

        private void Awake()
        {
            connector = GetComponent<LanConnector>();
            relayTransport = GetComponent<FishyUnityTransport>();
            connector.ServerStateChanged += OnServerStateChanged;
        }

        private void Start()
        {
            _ = EnsureServicesReadyAsync();
        }

        private void Update()
        {
            if (!IsLobbyOwner || currentLobby == null || Time.realtimeSinceStartup < nextHeartbeatTime) return;
            nextHeartbeatTime = Time.realtimeSinceStartup + LobbyHeartbeatSeconds;
            _ = SendHeartbeatAsync(currentLobby.Id);
        }

        private void OnDestroy()
        {
            if (connector != null) connector.ServerStateChanged -= OnServerStateChanged;
            if (currentLobby != null && Ready) _ = LeaveLobbyServiceAsync(currentLobby, IsLobbyOwner);
        }

        public void CreateLobby(string nickname)
        {
            _ = CreateRelayRoomAsync(nickname);
        }

        public void JoinRoomCode(string code)
        {
            _ = JoinRoomByCodeAsync(code);
        }

        public void JoinRoomById(string lobbyId)
        {
            _ = JoinRoomByIdAsync(lobbyId);
        }

        public void RefreshRooms()
        {
            _ = RefreshRoomsAsync();
        }

        public void Stop()
        {
            stopping = true;
            connector.Leave();
            LeaveLobby();
            Status = "Unity Relay 연결을 종료했습니다.";
        }

        public void LeaveLobby()
        {
            if (currentLobby == null || leavingLobby) return;
            LobbyModel lobby = currentLobby;
            bool wasOwner = IsLobbyOwner;
            currentLobby = null;
            IsLobbyOwner = false;
            LobbyOpen = false;
            leavingLobby = true;
            _ = LeaveLobbyServiceAsync(lobby, wasOwner);
        }

        public void LockLobbyForGame()
        {
            if (!IsLobbyOwner || currentLobby == null || !LobbyOpen) return;
            LobbyOpen = false;
            _ = LockLobbyAsync(currentLobby.Id);
        }

        private async Task<bool> EnsureServicesReadyAsync()
        {
            if (Ready) return true;
            if (initializationTask == null) initializationTask = InitializeServicesAsync();
            await initializationTask;
            return Ready;
        }

        private async Task InitializeServicesAsync()
        {
            try
            {
                Status = "Unity 서비스 초기화 중…";
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Ready = true;
                Status = "Unity Relay 준비됨";
            }
            catch (Exception e)
            {
                Status = "Unity 서비스 초기화 실패: " + e.GetBaseException().Message;
                Debug.LogWarning("[Unity Relay] " + Status, this);
            }
        }

        private async Task CreateRelayRoomAsync(string nickname)
        {
            if (creatingRoom || InLobby) return;
            creatingRoom = true;
            try
            {
                if (!await EnsureServicesReadyAsync()) throw new InvalidOperationException(Status);
                if (connector.Status == LanConnectionStatus.Connecting || connector.Status == LanConnectionStatus.Connected)
                    throw new InvalidOperationException("이미 다른 네트워크 방에 연결되어 있습니다.");
                if (relayTransport == null) throw new InvalidOperationException("FishyUnityTransport 컴포넌트가 없습니다.");

                Status = "Unity Relay 서버 할당 중…";
                var allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
                pendingRelayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                pendingLobbyName = CleanLobbyName(nickname) + "의 방";
                relayTransport.SetRelayServerData(allocation.ToRelayServerData("dtls"));

                publishLobbyWhenServerStarts = true;
                stopping = false;
                Status = "방 연결 준비 중…";
                if (!connector.HostRelay())
                {
                    publishLobbyWhenServerStarts = false;
                    throw new InvalidOperationException("FishNet Unity Relay 호스트를 시작할 수 없습니다.");
                }
            }
            catch (Exception e)
            {
                publishLobbyWhenServerStarts = false;
                Status = "방 생성 실패: " + e.GetBaseException().Message;
                Debug.LogWarning("[Unity Relay] " + Status, this);
            }
            finally
            {
                creatingRoom = false;
            }
        }

        private async Task PublishLobbyAsync()
        {
            if (!publishLobbyWhenServerStarts || string.IsNullOrEmpty(pendingRelayJoinCode)) return;
            publishLobbyWhenServerStarts = false;
            try
            {
                var data = new Dictionary<string, DataObject>
                {
                    [RelayCodeDataKey] = new DataObject(DataObject.VisibilityOptions.Member, pendingRelayJoinCode)
                };
                currentLobby = await LobbyService.Instance.CreateLobbyAsync(
                    pendingLobbyName,
                    MaxPlayers,
                    new CreateLobbyOptions { IsPrivate = false, Data = data });

                IsLobbyOwner = true;
                LobbyOpen = true;
                nextHeartbeatTime = Time.realtimeSinceStartup + LobbyHeartbeatSeconds;
                Status = string.IsNullOrEmpty(CurrentLobbyCode)
                    ? "방 생성됨 · 참가자를 기다리는 중입니다."
                    : "방 생성됨 · 참가 코드 " + CurrentLobbyCode;
                pendingRelayJoinCode = null;
                pendingLobbyName = null;
            }
            catch (Exception e)
            {
                Status = "방 목록 등록 실패: " + e.GetBaseException().Message;
                Debug.LogWarning("[Unity Lobby] " + Status, this);
                connector.Leave();
            }
        }

        private async Task JoinRoomByCodeAsync(string code)
        {
            if (InLobby) return;
            try
            {
                if (!await EnsureServicesReadyAsync()) throw new InvalidOperationException(Status);
                string normalizedCode = NormalizeLobbyCode(code);
                if (normalizedCode.Length < 4 || normalizedCode.Length > 8)
                    throw new InvalidOperationException("방 코드는 4~8자의 영문자와 숫자로 입력하세요.");

                Status = "방 코드 확인 중…";
                currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(normalizedCode);
                await ConnectToLobbyRelayAsync(currentLobby);
            }
            catch (Exception e)
            {
                Status = "방 참가 실패: " + e.GetBaseException().Message;
                Debug.LogWarning("[Unity Lobby] " + Status, this);
                LeaveLobby();
            }
        }

        private async Task JoinRoomByIdAsync(string lobbyId)
        {
            if (InLobby || string.IsNullOrEmpty(lobbyId)) return;
            try
            {
                if (!await EnsureServicesReadyAsync()) throw new InvalidOperationException(Status);
                Status = "선택한 방에 참가 중…";
                currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);
                await ConnectToLobbyRelayAsync(currentLobby);
            }
            catch (Exception e)
            {
                Status = "방 참가 실패: " + e.GetBaseException().Message;
                Debug.LogWarning("[Unity Lobby] " + Status, this);
                LeaveLobby();
            }
        }

        private async Task ConnectToLobbyRelayAsync(LobbyModel lobby)
        {
            if (relayTransport == null) throw new InvalidOperationException("FishyUnityTransport 컴포넌트가 없습니다.");
            if (lobby == null || lobby.Data == null || !lobby.Data.TryGetValue(RelayCodeDataKey, out DataObject relayCode)
                || string.IsNullOrEmpty(relayCode.Value))
                throw new InvalidOperationException("방의 Relay 연결 정보가 없습니다.");

            Status = "Unity Relay 방에 연결 중…";
            var allocation = await RelayService.Instance.JoinAllocationAsync(relayCode.Value);
            relayTransport.SetRelayServerData(allocation.ToRelayServerData("dtls"));
            stopping = false;
            if (!connector.JoinRelay()) throw new InvalidOperationException("FishNet 클라이언트 시작 요청이 거부됐습니다.");
        }

        private async Task RefreshRoomsAsync()
        {
            if (IsRefreshingRooms) return;
            IsRefreshingRooms = true;
            try
            {
                if (!await EnsureServicesReadyAsync()) throw new InvalidOperationException(Status);
                RoomListStatus = "방 목록 불러오는 중…";
                var result = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions { Count = 25 });
                availableRooms = result.Results
                    .Where(lobby => lobby.AvailableSlots > 0 && !lobby.IsLocked
                        && (currentLobby == null || lobby.Id != currentLobby.Id))
                    .ToList();
                RoomListStatus = availableRooms.Count == 0
                    ? "참가 가능한 공개 방이 없습니다. 코드를 입력하거나 새로고침하세요."
                    : "참가 가능한 방 " + availableRooms.Count + "개";
            }
            catch (Exception e)
            {
                RoomListStatus = "방 목록을 불러오지 못했습니다: " + e.GetBaseException().Message;
                Debug.LogWarning("[Unity Lobby] " + RoomListStatus, this);
            }
            finally
            {
                IsRefreshingRooms = false;
            }
        }

        private async Task SendHeartbeatAsync(string lobbyId)
        {
            try { await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId); }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity Lobby] 로비 heartbeat 실패: " + e.GetBaseException().Message, this);
            }
        }

        private async Task LockLobbyAsync(string lobbyId)
        {
            try
            {
                await LobbyService.Instance.UpdateLobbyAsync(lobbyId, new UpdateLobbyOptions { IsLocked = true });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity Lobby] 게임 시작 후 로비 잠금 실패: " + e.GetBaseException().Message, this);
            }
        }

        private async Task LeaveLobbyServiceAsync(LobbyModel lobby, bool wasOwner)
        {
            try
            {
                if (Ready && lobby != null)
                {
                    if (wasOwner) await LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
                    else await LobbyService.Instance.RemovePlayerAsync(lobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity Lobby] 방 나가기 처리 실패: " + e.GetBaseException().Message, this);
            }
            finally
            {
                IsLobbyOwner = false;
                LobbyOpen = false;
                leavingLobby = false;
            }
        }

        private void OnServerStateChanged(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started && publishLobbyWhenServerStarts)
                _ = PublishLobbyAsync();
            else if (args.ConnectionState == LocalConnectionState.Stopped && IsLobbyOwner && !stopping)
            {
                Status = "Unity Relay 호스트 연결이 종료됐습니다.";
                LeaveLobby();
            }
        }

        private static string CleanLobbyName(string nickname)
        {
            string clean = (nickname ?? string.Empty).Trim();
            if (clean.Length > 20) clean = clean.Substring(0, 20);
            return string.IsNullOrEmpty(clean) ? "Player" : clean;
        }

        private static string NormalizeLobbyCode(string code)
        {
            return new string((code ?? string.Empty)
                .Where(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                .Select(char.ToUpperInvariant)
                .ToArray());
        }
    }
}
