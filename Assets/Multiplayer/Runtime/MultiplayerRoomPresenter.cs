using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// RoomNetworkState의 서버 권한 팀 목록을 대기실 UI로 표현합니다.
    /// 버튼은 로컬 플레이어의 팀 변경을 요청할 뿐, 팀 목록을 직접 수정하지 않습니다.
    /// </summary>
    public sealed class MultiplayerRoomPresenter : MonoBehaviour
    {
        [SerializeField] private MultiplayerFlowController flowController;
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject createRoomPanel;
        [SerializeField] private GameObject joinRoomPanel;
        [SerializeField] private GameObject roomPanel;
        [SerializeField] private TMP_Text roomTitleText;
        [SerializeField] private TMP_Text joinCodeText;
        [SerializeField] private TMP_Text roomStatusText;
        [SerializeField] private Transform teamAList;
        [SerializeField] private Transform teamBList;
        [SerializeField] private GameObject playerEntryPrefab;
        [SerializeField] private Button teamAButton;
        [SerializeField] private Button teamBButton;
        [SerializeField] private Button startMatchButton;
        [SerializeField] private Button leaveRoomButton;

        private RoomNetworkState _roomState;

        private void OnEnable()
        {
            if (flowController != null)
            {
                flowController.StateChanged += OnFlowStateChanged;
                OnFlowStateChanged(flowController.State);
            }

            RoomNetworkState.Spawned += OnRoomStateSpawned;
            RoomNetworkState.Despawned += OnRoomStateDespawned;
            AttachRoomState(RoomNetworkState.LocalInstance);
        }

        private void OnDisable()
        {
            if (flowController != null)
            {
                flowController.StateChanged -= OnFlowStateChanged;
            }

            RoomNetworkState.Spawned -= OnRoomStateSpawned;
            RoomNetworkState.Despawned -= OnRoomStateDespawned;
            AttachRoomState(null);
        }

        public async void OnLeaveRoomPressed()
        {
            if (flowController == null)
            {
                return;
            }

            await LeaveRoomAsync();
        }

        /// <summary>Team A 영역을 클릭했을 때 로컬 플레이어의 Team A 이동을 서버에 요청합니다.</summary>
        public void OnTeamAPressed()
        {
            RequestTeamChange(RoomTeam.TeamA);
        }

        /// <summary>Team B 영역을 클릭했을 때 로컬 플레이어의 Team B 이동을 서버에 요청합니다.</summary>
        public void OnTeamBPressed()
        {
            RequestTeamChange(RoomTeam.TeamB);
        }

        public void OnStartMatchPressed()
        {
            if (_roomState == null || flowController == null || !flowController.IsHost)
            {
                return;
            }

            if (!_roomState.CanStartMatch())
            {
                SetStatus("Both teams must be full before starting.");
                return;
            }

            SetStatus("Starting match...");
            _roomState.RequestStartMatch();
        }

        private async Task LeaveRoomAsync()
        {
            SetStatus(flowController.IsHost ? "Closing room..." : "Leaving room...");
            await flowController.LeaveRoomAsync();
        }

        private void OnFlowStateChanged(MultiplayerFlowState state)
        {
            bool inRoom = state == MultiplayerFlowState.InRoom;
            if (roomPanel != null)
            {
                roomPanel.SetActive(inRoom);
            }

            if (inRoom)
            {
                SetPanelActive(mainMenuPanel, false);
                SetPanelActive(createRoomPanel, false);
                SetPanelActive(joinRoomPanel, false);
                if (joinCodeText != null)
                {
                    joinCodeText.text = flowController.JoinCode;
                }

                SetStatus(flowController.IsHost
                    ? "Lobby open. Leaving will close the room."
                    : "Lobby joined. Select the other team to request a move.");
                RefreshStartButton();
                RefreshPlayers();
            }
            else if (state == MultiplayerFlowState.Menu)
            {
                SetPanelActive(mainMenuPanel, true);
                ClearLists();
            }
        }

        private void OnRoomStateSpawned(RoomNetworkState roomState)
        {
            AttachRoomState(roomState);
        }

        private void OnRoomStateDespawned(RoomNetworkState roomState)
        {
            if (_roomState == roomState)
            {
                AttachRoomState(null);
            }
        }

        private void AttachRoomState(RoomNetworkState roomState)
        {
            if (_roomState == roomState)
            {
                return;
            }

            if (_roomState != null)
            {
                _roomState.PlayersChanged -= RefreshPlayers;
                _roomState.ModeChanged -= OnRoomModeChanged;
                _roomState.PlayersChanged -= RefreshStartButton;
                _roomState.PhaseChanged -= OnRoomPhaseChanged;
            }

            _roomState = roomState;

            if (_roomState != null)
            {
                _roomState.PlayersChanged += RefreshPlayers;
                _roomState.ModeChanged += OnRoomModeChanged;
                _roomState.PlayersChanged += RefreshStartButton;
                _roomState.PhaseChanged += OnRoomPhaseChanged;
            }

            RefreshRoomTitle();
            RefreshStartButton();
            RefreshPlayers();
        }

        private void OnRoomModeChanged(MatchMode mode)
        {
            RefreshRoomTitle();
        }

        private void OnRoomPhaseChanged(RoomPhase phase)
        {
            RefreshStartButton();
        }

        private void RefreshStartButton()
        {
            if (startMatchButton == null)
            {
                return;
            }

            bool isHost = flowController != null && flowController.IsHost;
            startMatchButton.gameObject.SetActive(isHost);
            startMatchButton.interactable = isHost && _roomState != null && _roomState.CanStartMatch();
        }

        private void RefreshRoomTitle()
        {
            if (roomTitleText == null || _roomState == null)
            {
                return;
            }

            roomTitleText.text = _roomState.Mode.Value == MatchMode.OneVsOne
                ? "1 vs 1"
                : "2 vs 2";
        }

        private void RefreshPlayers()
        {
            ClearLists();
            // The room state survives the NGO scene transition, while this presenter can
            // temporarily outlive its StartScene UI references during teardown.  Do not
            // let a late replicated player-list update dereference a destroyed panel.
            if (_roomState == null || roomPanel == null || !roomPanel.activeInHierarchy)
            {
                return;
            }

            foreach (RoomPlayerState player in _roomState.Players)
            {
                CreatePlayerButton(player);
            }
        }

        private void CreatePlayerButton(RoomPlayerState player)
        {
            Transform parent = player.Team == RoomTeam.TeamA ? teamAList : teamBList;
            if (parent == null)
            {
                return;
            }

            if (playerEntryPrefab == null)
            {
                Debug.LogError("[Lobby] Room player entry prefab is missing.");
                return;
            }

            GameObject entry = Instantiate(playerEntryPrefab, parent, false);
            entry.name = "PlayerEntry";
            var image = entry.GetComponent<Image>();
            var layoutElement = entry.GetComponent<LayoutElement>();
            var button = entry.GetComponent<Button>();
            var label = entry.GetComponentInChildren<TextMeshProUGUI>(true);
            if (image == null || layoutElement == null || button == null || label == null)
            {
                Debug.LogError("[Lobby] Room player entry prefab is missing a required UI component.");
                Destroy(entry);
                return;
            }

            image.color = player.ClientId == _roomState.NetworkManager.LocalClientId
                ? new Color(0.18f, 0.42f, 0.62f, 1f)
                : new Color(0.15f, 0.17f, 0.22f, 1f);
            label.text = player.DisplayName.ToString();

            // 행은 팀 선택 영역의 클릭을 가로채지 않습니다. 팀 컬럼 전체가 이동 요청의 대상입니다.
            button.onClick.RemoveAllListeners();
            button.interactable = false;
            image.raycastTarget = false;
        }

        private RoomTeam GetLocalTeam()
        {
            if (_roomState == null)
            {
                return RoomTeam.TeamA;
            }

            ulong localClientId = _roomState.NetworkManager.LocalClientId;
            foreach (RoomPlayerState player in _roomState.Players)
            {
                if (player.ClientId == localClientId)
                {
                    return player.Team;
                }
            }

            return RoomTeam.TeamA;
        }

        private void RequestTeamChange(RoomTeam requestedTeam)
        {
            if (_roomState == null)
            {
                return;
            }

            _roomState.RequestTeamChange(requestedTeam);
            SetStatus($"Requested move to {requestedTeam}. Waiting for server approval...");
        }

        private void ClearLists()
        {
            ClearList(teamAList);
            ClearList(teamBList);
        }

        private static void ClearList(Transform list)
        {
            if (list == null)
            {
                return;
            }

            for (int index = list.childCount - 1; index >= 0; index--)
            {
                Destroy(list.GetChild(index).gameObject);
            }
        }

        private static void SetPanelActive(GameObject panel, bool active)
        {
            if (panel != null)
            {
                panel.SetActive(active);
            }
        }

        private void SetStatus(string status)
        {
            if (roomStatusText != null)
            {
                roomStatusText.text = status;
            }
        }
    }
}
