using System;
using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// NGO NetworkManager의 실제 연결 수명을 관찰하는 클래스입니다.
    /// UGS Session 멤버십이나 재접속 정책은 관리하지 않고, 연결 사실만 상위 흐름에 알립니다.
    /// </summary>
    public sealed class NetworkLifecycle : MonoBehaviour
    {
        [SerializeField]
        private NetworkManager networkManager;

        // 같은 NetworkManager 콜백을 중복 구독하지 않기 위한 상태입니다.
        private bool _isSubscribed;

        /// <summary>
        /// NGO가 Transport를 열고 연결 요청을 받을 수 있는 상태인지 나타냅니다.
        /// 이 값은 UGS Session 참가 여부와는 별개입니다.
        /// </summary>
        public bool IsListening => networkManager != null && networkManager.IsListening;

        /// <summary>
        /// 이 로컬 인스턴스가 실제 NGO 클라이언트로 연결되었는지 나타냅니다.
        /// Host에서는 로컬 호스트 연결까지 완료된 뒤 true가 됩니다.
        /// </summary>
        public bool IsLocalClientConnected =>
            networkManager != null && networkManager.IsConnectedClient;

        // NGO 관점에서 연결된 ClientId를 전달합니다. Host에서는 원격 참가자도 전달받습니다.
        public event Action<ulong> ClientConnected;

        // NGO 연결이 끊긴 ClientId와, 콜백 시점에 캡처한 해제 사유를 전달합니다.
        public event Action<ulong, string> ClientDisconnected;

        // 현재 기기 자신의 NGO 연결이 해제된 경우에만 발생합니다.
        public event Action<string> LocalClientDisconnected;

        private void Awake()
        {
            // 권장 구성은 MultiplayerBootstrap에 배치한 NetworkManager를 Inspector에서 연결하는 방식입니다.
            if (networkManager == null)
            {
                networkManager = NetworkManager.Singleton;
            }

            if (networkManager == null)
            {
                Debug.LogError(
                    "[NGO] NetworkManager reference is missing. " +
                    "Assign the bootstrap NetworkManager.");
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            // 비활성화 및 파괴 시 모두 콜백을 해제해 중복 로그와 이벤트 누수를 막습니다.
            Unsubscribe();
        }

        /// <summary>
        /// NetworkManager의 NGO 연결/해제 콜백을 한 번만 구독합니다.
        /// </summary>
        private void Subscribe()
        {
            if (_isSubscribed || networkManager == null)
            {
                return;
            }

            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            _isSubscribed = true;
        }

        /// <summary>
        /// 이 클래스가 구독한 NetworkManager 콜백을 정확히 해제합니다.
        /// </summary>
        private void Unsubscribe()
        {
            if (!_isSubscribed || networkManager == null)
            {
                return;
            }

            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            _isSubscribed = false;
        }

        /// <summary>
        /// NGO 연결 승인 및 연결 완료 뒤 호출됩니다.
        /// 클라이언트에서는 자기 자신만, 서버/호스트에서는 각 참가자의 연결도 관찰합니다.
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            bool isLocalClient = clientId == networkManager.LocalClientId;

            if (isLocalClient)
            {
                string role = networkManager.IsHost ? "host" : "client";
                Debug.Log($"[NGO] Local {role} connected. ClientId={clientId}");
            }
            else
            {
                Debug.Log($"[NGO] Remote client connected. ClientId={clientId}");
            }

            ClientConnected?.Invoke(clientId);
        }

        /// <summary>
        /// NGO Transport 또는 서버가 연결 해제를 확정한 시점에 호출됩니다.
        /// DisconnectReason은 콜백 시점에 읽어야 하므로 즉시 문자열로 복사합니다.
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            bool isLocalClient = clientId == networkManager.LocalClientId;
            string reason = GetDisconnectReason();

            if (isLocalClient)
            {
                Debug.Log(
                    $"[NGO] Local client disconnected. " +
                    $"ClientId={clientId} Reason={reason}");
            }
            else
            {
                Debug.Log(
                    $"[NGO] Remote client disconnected. " +
                    $"ClientId={clientId} Reason={reason}");
            }

            ClientDisconnected?.Invoke(clientId, reason);

            // 상위 FlowController가 예상된 Leave인지, 예기치 못한 연결 해제인지 판단합니다.
            if (isLocalClient)
            {
                LocalClientDisconnected?.Invoke(reason);
            }
        }

        // NGO가 제공한 사유가 없다면 로그에서 구분 가능한 기본 문구를 사용합니다.
        private string GetDisconnectReason()
        {
            string reason = networkManager.DisconnectReason;
            return string.IsNullOrWhiteSpace(reason)
                ? "No disconnect reason provided."
                : reason;
        }
    }
}
