using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// 호스트만 실행할 방 규칙의 진입점입니다.
    /// 이번 MVP에서는 RoomNetworkState의 초기화만 담당합니다.
    /// </summary>
    [RequireComponent(typeof(RoomNetworkState))]
    public sealed class RoomAuthority : NetworkBehaviour
    {
        private RoomNetworkState _roomNetworkState;

        private void Awake()
        {
            // 같은 프리팹의 RoomNetworkState를 사용하므로 Inspector 수동 연결이 필요 없습니다.
            _roomNetworkState = GetComponent<RoomNetworkState>();
        }

        /// <summary>
        /// 서버가 새로 Spawn한 방의 모드와 초기 단계를 결정합니다.
        /// 이후 팀·Ready·게임 시작 검증도 이 권한 계층에 추가됩니다.
        /// </summary>
        public void InitializeRoom(MatchMode mode)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[Room] Only the server can initialize a room.");
                return;
            }

            if (_roomNetworkState == null)
            {
                Debug.LogError("[Room] RoomNetworkState component is missing.");
                return;
            }

            // 모드 정원을 중앙 규칙에서 검증해 알 수 없는 enum 값은 즉시 드러나게 합니다.
            MatchModeRules.For(mode);
            _roomNetworkState.InitializeServerState(mode);
        }
    }
}
