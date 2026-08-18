using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Unity.Collections;
using Unity.Services.Authentication;
using UnityEngine.SceneManagement;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// 호스트가 작성하고 모든 NGO 클라이언트가 읽는 방 상태입니다.
    /// UGS Session 정보나 실시간 플레이어 상태는 보관하지 않습니다.
    /// </summary>
    public sealed class RoomNetworkState : NetworkBehaviour
    {
        private const string MainSceneName = "MainScene";

        // 각 실행 인스턴스에서 현재 복제되어 있는 단 하나의 방 상태 객체입니다.
        // FlowController가 클라이언트의 방 상태 수신 완료를 확인할 때 사용합니다.
        public static RoomNetworkState LocalInstance { get; private set; }

        // FlowController가 RoomNetworkState 수신을 비동기로 기다릴 수 있도록 알립니다.
        public static event System.Action<RoomNetworkState> Spawned;
        public static event System.Action<RoomNetworkState> Despawned;

        // 방 모드는 호스트만 쓰고, 접속한 모든 클라이언트가 읽습니다.
        public NetworkVariable<MatchMode> Mode { get; } =
            new NetworkVariable<MatchMode>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        // 방 진행 단계도 호스트만 전환하며, 현재 MVP에서는 Waiting만 사용합니다.
        public NetworkVariable<RoomPhase> Phase { get; } =
            new NetworkVariable<RoomPhase>(
                RoomPhase.Waiting,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        // 서버만 목록을 변경하고, 모든 참가자는 같은 순서와 팀 배정을 읽습니다.
        public NetworkList<RoomPlayerState> Players { get; } = new NetworkList<RoomPlayerState>();

        public event System.Action PlayersChanged;
        public event System.Action<MatchMode> ModeChanged;
        public event System.Action<RoomPhase> PhaseChanged;

        private readonly HashSet<ulong> _mainSceneReadyClientIds = new();

        public override void OnNetworkSpawn()
        {
            LocalInstance = this;
            SceneManager.sceneLoaded += OnUnitySceneLoaded;

            // NetworkVariable 변경을 관찰해 호스트와 클라이언트 모두 동일한 값을 로그로 확인합니다.
            Mode.OnValueChanged += OnModeChanged;
            Phase.OnValueChanged += OnPhaseChanged;
            Players.OnListChanged += OnPlayersListChanged;

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }

            if (IsServer)
            {
                Debug.Log("[Room] RoomNetworkState spawned by server");
            }
            else
            {
                Debug.Log("[Room] RoomNetworkState received by client");
            }

            // 늦게 구독한 경우에도 현재 복제된 초기값을 한 번 기록합니다.
            Debug.Log($"[Room] Current state. Mode={Mode.Value} Phase={Phase.Value}");
            Spawned?.Invoke(this);

            // 호스트는 즉시, 클라이언트는 복제본을 받은 직후 자신의 표시 이름을 등록합니다.
            RegisterLocalPlayer();
        }

        public override void OnNetworkDespawn()
        {
            SceneManager.sceneLoaded -= OnUnitySceneLoaded;

            // 프리팹이 제거될 때 NetworkVariable 콜백을 반드시 해제합니다.
            Mode.OnValueChanged -= OnModeChanged;
            Phase.OnValueChanged -= OnPhaseChanged;
            Players.OnListChanged -= OnPlayersListChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            if (LocalInstance == this)
            {
                LocalInstance = null;
            }

            Despawned?.Invoke(this);
        }

        public bool IsClientMainSceneReady(ulong clientId)
        {
            return IsServer && _mainSceneReadyClientIds.Contains(clientId);
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            if (scene.name == MainSceneName)
            {
                StartCoroutine(ReportMainSceneReadyAfterActivation());
            }
        }

        private IEnumerator ReportMainSceneReadyAfterActivation()
        {
            // sceneLoaded fires before every Start callback and before the first rendered frame.
            // Wait until the scene has completed one full frame before declaring this peer ready.
            yield return null;
            yield return new WaitForEndOfFrame();

            if (!IsSpawned || NetworkManager == null || !NetworkManager.IsListening)
            {
                yield break;
            }

            if (IsServer)
            {
                MarkClientMainSceneReady(NetworkManager.LocalClientId);
            }
            else
            {
                ReportMainSceneReadyServerRpc();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportMainSceneReadyServerRpc(ServerRpcParams rpcParams = default)
        {
            MarkClientMainSceneReady(rpcParams.Receive.SenderClientId);
        }

        private void MarkClientMainSceneReady(ulong clientId)
        {
            if (!IsServer || !NetworkManager.ConnectedClients.ContainsKey(clientId))
            {
                return;
            }

            if (_mainSceneReadyClientIds.Add(clientId))
            {
                Debug.Log($"[Room] Client {clientId} confirmed MainScene activation.");
            }
        }

        /// <summary>
        /// 서버가 프리팹을 Spawn한 직후 초기 방 상태를 설정합니다.
        /// 클라이언트 호출은 거부해 쓰기 권한을 우회하지 못하게 합니다.
        /// </summary>
        public void InitializeServerState(MatchMode mode)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[Room] Only the server can initialize room state.");
                return;
            }

            // 이후 RoomAuthority가 검증한 모드와 대기 단계를 모든 클라이언트에 복제합니다.
            Mode.Value = mode;
            Phase.Value = RoomPhase.Waiting;

            Debug.Log($"[Room] Initialized. Mode={Mode.Value} Phase={Phase.Value}");
        }

        private void OnModeChanged(MatchMode previousValue, MatchMode newValue)
        {
            Debug.Log($"[Room] Mode changed. {previousValue} -> {newValue}");
            ModeChanged?.Invoke(newValue);
        }

        private void OnPhaseChanged(RoomPhase previousValue, RoomPhase newValue)
        {
            Debug.Log($"[Room] Phase changed. {previousValue} -> {newValue}");
            PhaseChanged?.Invoke(newValue);
        }

        /// <summary>
        /// 로컬 사용자가 다른 팀을 선택하면 서버에 요청합니다. 실제 변경은 서버에서만 수행됩니다.
        /// </summary>
        public void RequestTeamChange(RoomTeam requestedTeam)
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                TryChangeTeam(NetworkManager.LocalClientId, requestedTeam);
                return;
            }

            RequestTeamChangeServerRpc(requestedTeam);
        }

        /// <summary>
        /// 방장의 Start 요청은 서버가 팀 정원과 방 단계를 검증한 뒤 MainScene을 모든 NGO 클라이언트에 로드합니다.
        /// </summary>
        public void RequestStartMatch()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                TryStartMatch(NetworkManager.LocalClientId);
                return;
            }

            RequestStartMatchServerRpc();
        }

        public bool CanStartMatch()
        {
            if (Phase.Value != RoomPhase.Waiting)
            {
                return false;
            }

            MatchModeRules rules = MatchModeRules.For(Mode.Value);
            return CountPlayers(RoomTeam.TeamA) == rules.TeamACapacity &&
                   CountPlayers(RoomTeam.TeamB) == rules.TeamBCapacity;
        }

        private void RegisterLocalPlayer()
        {
            string playerId = AuthenticationService.Instance.IsSignedIn
                ? AuthenticationService.Instance.PlayerId
                : string.Empty;
            string displayName = string.IsNullOrWhiteSpace(playerId)
                ? $"Player {NetworkManager.LocalClientId}"
                : $"Player {playerId.Substring(System.Math.Max(0, playerId.Length - 6))}";
            var fixedName = new FixedString64Bytes(displayName);

            if (IsServer)
            {
                AddOrUpdatePlayer(NetworkManager.LocalClientId, fixedName);
            }
            else
            {
                RegisterPlayerServerRpc(fixedName);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void RegisterPlayerServerRpc(
            FixedString64Bytes displayName,
            ServerRpcParams rpcParams = default)
        {
            AddOrUpdatePlayer(rpcParams.Receive.SenderClientId, displayName);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestTeamChangeServerRpc(
            RoomTeam requestedTeam,
            ServerRpcParams rpcParams = default)
        {
            TryChangeTeam(rpcParams.Receive.SenderClientId, requestedTeam);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestStartMatchServerRpc(ServerRpcParams rpcParams = default)
        {
            TryStartMatch(rpcParams.Receive.SenderClientId);
        }

        private void OnClientConnected(ulong clientId)
        {
            // 이름 등록 RPC보다 먼저 도착할 수 있으므로 임시 이름을 넣습니다.
            AddOrUpdatePlayer(clientId, new FixedString64Bytes($"Player {clientId}"));
        }

        private void OnClientDisconnected(ulong clientId)
        {
            for (int index = Players.Count - 1; index >= 0; index--)
            {
                if (Players[index].ClientId == clientId)
                {
                    Players.RemoveAt(index);
                    break;
                }
            }
        }

        private void AddOrUpdatePlayer(ulong clientId, FixedString64Bytes displayName)
        {
            if (!IsServer)
            {
                return;
            }

            for (int index = 0; index < Players.Count; index++)
            {
                if (Players[index].ClientId != clientId)
                {
                    continue;
                }

                RoomPlayerState existing = Players[index];
                existing.DisplayName = displayName;
                Players[index] = existing;
                return;
            }

            Players.Add(new RoomPlayerState(clientId, displayName, GetAvailableTeam()));
        }

        private bool TryChangeTeam(ulong clientId, RoomTeam requestedTeam)
        {
            if (!IsServer || Phase.Value != RoomPhase.Waiting)
            {
                return false;
            }

            for (int index = 0; index < Players.Count; index++)
            {
                RoomPlayerState player = Players[index];
                if (player.ClientId != clientId)
                {
                    continue;
                }

                if (player.Team == requestedTeam || !HasTeamCapacity(requestedTeam))
                {
                    return false;
                }

                player.Team = requestedTeam;
                Players[index] = player;
                return true;
            }

            return false;
        }

        private bool TryStartMatch(ulong requestingClientId)
        {
            if (!IsServer || requestingClientId != NetworkManager.ServerClientId)
            {
                Debug.LogWarning("[Room] Only the host can start the match.");
                return false;
            }

            if (!CanStartMatch())
            {
                Debug.LogWarning("[Room] Match start rejected: both teams must be full.");
                return false;
            }

            if (NetworkManager.SceneManager == null)
            {
                Debug.LogError("[Room] NGO scene management is disabled.");
                return false;
            }

            _mainSceneReadyClientIds.Clear();
            Phase.Value = RoomPhase.Starting;
            SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(
                "MainScene",
                UnityEngine.SceneManagement.LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                Phase.Value = RoomPhase.Waiting;
                Debug.LogError($"[Room] MainScene load was not started. Status={status}");
                return false;
            }

            Debug.Log("[Room] Match start accepted. Loading MainScene for all clients.");
            return true;
        }

        private RoomTeam GetAvailableTeam()
        {
            int teamACount = CountPlayers(RoomTeam.TeamA);
            int teamBCount = CountPlayers(RoomTeam.TeamB);
            MatchModeRules rules = MatchModeRules.For(Mode.Value);

            if (teamACount < rules.TeamACapacity &&
                (teamACount <= teamBCount || teamBCount >= rules.TeamBCapacity))
            {
                return RoomTeam.TeamA;
            }

            return RoomTeam.TeamB;
        }

        private bool HasTeamCapacity(RoomTeam team)
        {
            MatchModeRules rules = MatchModeRules.For(Mode.Value);
            return team == RoomTeam.TeamA
                ? CountPlayers(team) < rules.TeamACapacity
                : CountPlayers(team) < rules.TeamBCapacity;
        }

        private int CountPlayers(RoomTeam team)
        {
            int count = 0;
            for (int index = 0; index < Players.Count; index++)
            {
                if (Players[index].Team == team)
                {
                    count++;
                }
            }

            return count;
        }

        private void OnPlayersListChanged(NetworkListEvent<RoomPlayerState> changeEvent)
        {
            PlayersChanged?.Invoke();
        }
    }
}
