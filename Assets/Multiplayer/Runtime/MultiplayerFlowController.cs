using System;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// 사용자의 방 생성·입장·퇴장 의도를 UGS Session과 NGO 연결 흐름으로 조합합니다.
    /// 직접 UGS API를 호출하지 않으며, SessionFacade와 NetworkLifecycle의 결과를 순서대로 처리합니다.
    /// </summary>
    public sealed class MultiplayerFlowController : MonoBehaviour
    {
        // Relay/NGO 연결 및 방 상태 복제를 기다릴 최대 시간입니다.
        private static readonly TimeSpan ConnectionWaitTimeout = TimeSpan.FromSeconds(15);

        // NetworkManager.Shutdown() completes on a later network-loop update.  Keep the
        // room flow busy until that teardown has actually finished, otherwise a new Join
        // can overlap the Sessions network module's previous stop operation.
        private static readonly TimeSpan ShutdownWaitTimeout = TimeSpan.FromSeconds(15);

        [SerializeField]
        private UgsBootstrap ugsBootstrap;

        [SerializeField]
        private SessionFacade sessionFacade;

        [SerializeField]
        private NetworkLifecycle networkLifecycle;

        // 호스트만 Instantiate와 Spawn을 수행할 등록된 네트워크 프리팹입니다.
        [SerializeField]
        private RoomNetworkState roomNetworkStatePrefab;

        // Create / Join / Leave를 한 번에 하나만 실행하도록 보장합니다.
        private bool _isFlowOperation;

        // Leave 과정에서 발생하는 정상적인 NGO 해제를 예기치 못한 해제로 처리하지 않기 위한 플래그입니다.
        private bool _isExpectedLeave;

        // 이 로컬 실행 인스턴스가 참조하는 호스트 Spawn 방 상태입니다.
        private RoomNetworkState _roomNetworkState;

        public MultiplayerFlowState State { get; private set; } =
            MultiplayerFlowState.Offline;

        public string LastError { get; private set; } = string.Empty;

        public string JoinCode => sessionFacade != null ? sessionFacade.JoinCode : string.Empty;

        public bool IsHost => sessionFacade != null && sessionFacade.IsHost;

        public bool IsOperationInProgress => _isFlowOperation;

        public event Action<MultiplayerFlowState> StateChanged;

        private void Awake()
        {
            ValidateDependencies();

            // 이 루트는 미래의 네트워크 씬 전환 후에도 살아 있어야 합니다.
            DontDestroyOnLoad(gameObject);
            SetState(MultiplayerFlowState.Menu);
        }

        private void OnEnable()
        {
            if (networkLifecycle != null)
            {
                networkLifecycle.LocalClientDisconnected += OnLocalClientDisconnected;
            }

            if (sessionFacade != null)
            {
                sessionFacade.SessionChanged += OnSessionChanged;
            }

            RoomNetworkState.Spawned += OnRoomNetworkStateSpawned;
            RoomNetworkState.Despawned += OnRoomNetworkStateDespawned;
        }

        private void OnDisable()
        {
            if (networkLifecycle != null)
            {
                networkLifecycle.LocalClientDisconnected -= OnLocalClientDisconnected;
            }

            if (sessionFacade != null)
            {
                sessionFacade.SessionChanged -= OnSessionChanged;
            }

            RoomNetworkState.Spawned -= OnRoomNetworkStateSpawned;
            RoomNetworkState.Despawned -= OnRoomNetworkStateDespawned;
        }

        /// <summary>
        /// 호스트 방을 만들고 Relay/NGO 연결 완료 후 RoomNetworkState를 한 번 Spawn합니다.
        /// </summary>
        public async Task CreateRoomAsync(MatchMode mode)
        {
            if (!TryBeginFlowOperation())
            {
                return;
            }

            try
            {
                SetState(MultiplayerFlowState.Initializing);
                await ugsBootstrap.EnsureReadyAsync();

                SetState(MultiplayerFlowState.CreatingRoom);
                SessionOperationResult createResult =
                    await sessionFacade.CreateSessionAsync(mode);

                if (!createResult.Success)
                {
                    SetError(createResult.Message);
                    return;
                }

                // Sessions 네트워크 모듈이 시작한 NGO 로컬 호스트 연결을 확인합니다.
                if (!await WaitForLocalNgoConnectionAsync())
                {
                    await CleanupAfterIncompleteFlowAsync(
                        "Timed out waiting for local host NGO connection.");
                    return;
                }

                SpawnAndInitializeRoomNetworkState(mode);
                SetState(MultiplayerFlowState.InRoom);

                Debug.Log(
                    $"[Flow] State=InRoom SessionId={sessionFacade.SessionId} " +
                    $"Code={sessionFacade.JoinCode}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                await CleanupAfterIncompleteFlowAsync(
                    "Unable to create the room. Check the console for details.");
            }
            finally
            {
                _isFlowOperation = false;
            }
        }

        /// <summary>
        /// Join Code로 방에 입장하고, NGO 연결 및 호스트 방 상태 복제까지 기다립니다.
        /// </summary>
        public async Task JoinRoomAsync(string code)
        {
            if (!TryBeginFlowOperation())
            {
                return;
            }

            try
            {
                SetState(MultiplayerFlowState.Initializing);
                await ugsBootstrap.EnsureReadyAsync();

                SetState(MultiplayerFlowState.JoiningRoom);
                SessionOperationResult joinResult =
                    await sessionFacade.JoinSessionByCodeAsync(code);

                if (!joinResult.Success)
                {
                    SetError(joinResult.Message);
                    return;
                }

                // UGS Session 참가만으로는 게임 연결 완료가 아니므로 NGO 콜백을 기다립니다.
                if (!await WaitForLocalNgoConnectionAsync())
                {
                    await CleanupAfterIncompleteFlowAsync(
                        "Timed out waiting for local client NGO connection.");
                    return;
                }

                // 클라이언트는 호스트가 Spawn한 단 하나의 RoomNetworkState를 수신해야 합니다.
                if (!await WaitForRoomNetworkStateAsync())
                {
                    await CleanupAfterIncompleteFlowAsync(
                        "Timed out waiting for the room state from the host.");
                    return;
                }

                SetState(MultiplayerFlowState.InRoom);
                Debug.Log(
                    $"[Flow] State=InRoom SessionId={sessionFacade.SessionId} " +
                    $"Mode={_roomNetworkState.Mode.Value} " +
                    $"Phase={_roomNetworkState.Phase.Value}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                await CleanupAfterIncompleteFlowAsync(
                    "Unable to join the room. Check the console for details.");
            }
            finally
            {
                _isFlowOperation = false;
            }
        }

        /// <summary>
        /// 현재 방을 정상 종료합니다. 인증 상태는 유지하고 메뉴로만 돌아갑니다.
        /// </summary>
        public async Task LeaveRoomAsync()
        {
            if (!TryBeginFlowOperation())
            {
                return;
            }

            _isExpectedLeave = true;

            try
            {
                SetState(MultiplayerFlowState.LeavingRoom);
                SessionOperationResult leaveResult = await sessionFacade.LeaveSessionAsync();

                NetworkManager networkManager = NetworkManager.Singleton;
                if (networkManager != null && networkManager.IsListening &&
                    !networkManager.ShutdownInProgress)
                {
                    networkManager.Shutdown();
                }

                bool shutdownCompleted = await WaitForNgoShutdownAsync(networkManager);
                if (!shutdownCompleted)
                {
                    Debug.LogWarning("[Flow] Timed out waiting for NGO shutdown while leaving room.");
                }

                ClearRoomStateReference();

                if (!leaveResult.Success)
                {
                    SetError(leaveResult.Message);
                    return;
                }

                LastError = string.Empty;
                SetState(MultiplayerFlowState.Menu);
                Debug.Log("[Flow] Session cleanup complete. State=Menu");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetError("Unable to leave the room. Check the console for details.");
            }
            finally
            {
                _isExpectedLeave = false;
                _isFlowOperation = false;
            }
        }

        /// <summary>
        /// NGO의 로컬 연결 완료 이벤트를 기다립니다.
        /// 이미 연결된 경우에는 이벤트를 기다리지 않고 즉시 성공합니다.
        /// </summary>
        private async Task<bool> WaitForLocalNgoConnectionAsync()
        {
            double deadline =
                Time.realtimeSinceStartupAsDouble + ConnectionWaitTimeout.TotalSeconds;

            while (!networkLifecycle.IsLocalClientConnected)
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    return false;
                }

                await Awaitable.NextFrameAsync();
            }

            return true;
        }

        /// <summary>
        /// 호스트가 Spawn한 RoomNetworkState가 이 클라이언트에 복제될 때까지 기다립니다.
        /// </summary>
        private async Task<bool> WaitForRoomNetworkStateAsync()
        {
            double deadline =
                Time.realtimeSinceStartupAsDouble + ConnectionWaitTimeout.TotalSeconds;

            while (RoomNetworkState.LocalInstance == null)
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    return false;
                }

                await Awaitable.NextFrameAsync();
            }

            _roomNetworkState = RoomNetworkState.LocalInstance;
            return true;
        }

        /// <summary>
        /// 호스트만 RoomNetworkState 프리팹을 생성하고 NetworkObject로 Spawn합니다.
        /// 클라이언트는 이 메서드를 호출하지 않고 복제본만 수신합니다.
        /// </summary>
        private void SpawnAndInitializeRoomNetworkState(MatchMode mode)
        {
            if (!sessionFacade.IsHost)
            {
                throw new InvalidOperationException("Only the host can spawn room state.");
            }

            if (roomNetworkStatePrefab == null)
            {
                throw new InvalidOperationException(
                    "RoomNetworkState prefab is not assigned to MultiplayerFlowController.");
            }

            if (RoomNetworkState.LocalInstance != null)
            {
                throw new InvalidOperationException("A RoomNetworkState is already spawned.");
            }

            // 등록된 프리팹에서만 생성해 NGO가 모든 클라이언트에 같은 NetworkObject를 복제하게 합니다.
            _roomNetworkState = Instantiate(roomNetworkStatePrefab);

            NetworkObject networkObject = _roomNetworkState.GetComponent<NetworkObject>();
            RoomAuthority roomAuthority = _roomNetworkState.GetComponent<RoomAuthority>();

            if (networkObject == null || roomAuthority == null)
            {
                Destroy(_roomNetworkState.gameObject);
                _roomNetworkState = null;

                throw new InvalidOperationException(
                    "RoomNetworkState prefab requires NetworkObject and RoomAuthority components.");
            }

            networkObject.Spawn();
            roomAuthority.InitializeRoom(mode);
        }

        /// <summary>
        /// 정상 Leave가 아닌 로컬 NGO 해제를 감지하면 남은 Session 핸들을 정리합니다.
        /// </summary>
        private async void OnLocalClientDisconnected(string reason)
        {
            if (_isExpectedLeave || _isFlowOperation || !sessionFacade.HasSession)
            {
                return;
            }

            _isFlowOperation = true;

            try
            {
                Debug.LogWarning($"[Flow] Unexpected local NGO disconnect. Reason={reason}");
                await sessionFacade.LeaveSessionAsync();

                ClearRoomStateReference();
                LastError = $"Network connection closed: {reason}";
                SetState(MultiplayerFlowState.Menu);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ClearRoomStateReference();
                SetError($"Network connection closed: {reason}");
            }
            finally
            {
                _isFlowOperation = false;
            }
        }

        /// <summary>
        /// 호스트가 방을 삭제했거나 서버가 로컬 참가자를 제거한 경우 SessionFacade가 핸들을
        /// 정리한 뒤 알립니다. NGO 해제 순서와 무관하게 메뉴로 돌아가도록 여기서 처리합니다.
        /// </summary>
        private void OnSessionChanged()
        {
            if (sessionFacade == null || sessionFacade.HasSession ||
                State == MultiplayerFlowState.Menu || State == MultiplayerFlowState.LeavingRoom)
            {
                return;
            }

            HandleSessionEndedByHost();
        }

        /// <summary>
        /// When the host deletes a session, the Session SDK removes the local membership before
        /// NGO necessarily closes its Relay connection. Shut NGO down as well so a later Join
        /// begins from a completely idle Sessions/Relay state.
        /// </summary>
        private async void HandleSessionEndedByHost()
        {
            if (_isFlowOperation)
            {
                return;
            }

            _isFlowOperation = true;
            _isExpectedLeave = true;

            try
            {
                SetState(MultiplayerFlowState.LeavingRoom);

                NetworkManager networkManager = NetworkManager.Singleton;
                if (networkManager != null && networkManager.IsListening &&
                    !networkManager.ShutdownInProgress)
                {
                    networkManager.Shutdown();
                }

                bool shutdownCompleted = await WaitForNgoShutdownAsync(networkManager);
                if (!shutdownCompleted)
                {
                    Debug.LogWarning(
                        "[Flow] Timed out waiting for NGO shutdown after the host ended the session.");
                }

                ClearRoomStateReference();
                LastError = string.Empty;
                SetState(MultiplayerFlowState.Menu);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ClearRoomStateReference();
                SetError("The host closed the room, but network cleanup failed.");
            }
            finally
            {
                _isExpectedLeave = false;
                _isFlowOperation = false;
            }
        }

        /// <summary>
        /// NGO shutdown is deferred to a later network-loop update.  Waiting for both
        /// flags prevents the next Sessions join from reusing NetworkManager while the
        /// previous Relay session is still stopping.
        /// </summary>
        private static async Task<bool> WaitForNgoShutdownAsync(NetworkManager networkManager)
        {
            if (networkManager == null)
            {
                return true;
            }

            double deadline =
                Time.realtimeSinceStartupAsDouble + ShutdownWaitTimeout.TotalSeconds;

            while (networkManager != null &&
                   (networkManager.IsListening || networkManager.ShutdownInProgress))
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    return false;
                }

                await Awaitable.NextFrameAsync();
            }

            // Allow the Sessions network module continuation that awaited NGO shutdown
            // to clear its previous handler before the menu enables Create/Join again.
            await Awaitable.NextFrameAsync();
            return true;
        }

        // RoomNetworkState의 정적 이벤트는 수신 완료와 참조 정리에만 사용합니다.
        private void OnRoomNetworkStateSpawned(RoomNetworkState roomNetworkState)
        {
            _roomNetworkState = roomNetworkState;
        }

        private void OnRoomNetworkStateDespawned(RoomNetworkState roomNetworkState)
        {
            if (_roomNetworkState == roomNetworkState)
            {
                _roomNetworkState = null;
            }
        }

        private bool TryBeginFlowOperation()
        {
            if (_isFlowOperation)
            {
                Debug.LogWarning("[Flow] A multiplayer flow operation is already in progress.");
                return false;
            }

            if (ugsBootstrap == null || sessionFacade == null || networkLifecycle == null)
            {
                SetError("Multiplayer bootstrap dependencies are not configured.");
                return false;
            }

            _isFlowOperation = true;
            LastError = string.Empty;
            return true;
        }

        private async Task CleanupAfterIncompleteFlowAsync(string errorMessage)
        {
            Debug.LogWarning($"[Flow] {errorMessage}");

            // 세션 생성 또는 참가 뒤 일부 단계가 실패한 경우 남은 멤버십을 정리합니다.
            if (sessionFacade.HasSession)
            {
                _isExpectedLeave = true;

                try
                {
                    await sessionFacade.LeaveSessionAsync();
                }
                finally
                {
                    _isExpectedLeave = false;
                }
            }

            ClearRoomStateReference();
            SetError(errorMessage);
        }

        private void ClearRoomStateReference()
        {
            // NetworkObject의 Destroy/Despawn은 Session 네트워크 모듈의 정상 종료에 맡깁니다.
            _roomNetworkState = null;
        }

        private void SetState(MultiplayerFlowState nextState)
        {
            State = nextState;
            Debug.Log($"[Flow] State={State}");
            StateChanged?.Invoke(State);
        }

        private void SetError(string message)
        {
            LastError = message;
            Debug.LogError($"[Flow] {message}");
            SetState(MultiplayerFlowState.Error);
        }

        private void ValidateDependencies()
        {
            if (ugsBootstrap == null || sessionFacade == null || networkLifecycle == null)
            {
                Debug.LogError(
                    "[Flow] Assign UgsBootstrap, SessionFacade, and NetworkLifecycle " +
                    "on MultiplayerBootstrap.");
            }
        }
    }
}
