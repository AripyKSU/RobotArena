using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// 방에 들어가기 전 메뉴의 Create/Join 입력만 표현하는 UI Presenter입니다.
    /// 팀 선택, Ready, 게임 시작 같은 방 내부 UI는 미래의 MultiplayerRoomPresenter가 담당합니다.
    /// </summary>
    public sealed class MultiplayerMenuPresenter : MonoBehaviour
    {
        [SerializeField]
        private MultiplayerFlowController flowController;

        [Header("Create Room")]
        [SerializeField]
        private Button createOneVsOneButton;

        [SerializeField]
        private Button createTwoVsTwoButton;

        [Header("Join Room")]
        [SerializeField]
        private TMP_InputField joinCodeInput;

        [SerializeField]
        private Button joinRoomButton;

        [Header("Feedback")]
        [SerializeField]
        private TMP_Text statusText;

        private void Awake()
        {
            if (flowController == null)
            {
                Debug.LogError(
                    "[Menu] MultiplayerFlowController reference is missing. " +
                    "Assign the MultiplayerBootstrap FlowController.");
            }
        }

        private void OnEnable()
        {
            if (flowController == null)
            {
                return;
            }

            // UI는 Flow 상태만 관찰하며 UGS, SessionFacade, NetworkManager를 직접 호출하지 않습니다.
            flowController.StateChanged += OnFlowStateChanged;
            RefreshView(flowController.State);
        }

        private void OnDisable()
        {
            if (flowController != null)
            {
                flowController.StateChanged -= OnFlowStateChanged;
            }
        }

        /// <summary>
        /// 1 대 1 방 생성 버튼의 Inspector OnClick 이벤트에 연결합니다.
        /// </summary>
        public async void OnCreateOneVsOnePressed()
        {
            await CreateRoomAsync(MatchMode.OneVsOne);
        }

        /// <summary>
        /// 2 대 2 방 생성 버튼의 Inspector OnClick 이벤트에 연결합니다.
        /// </summary>
        public async void OnCreateTwoVsTwoPressed()
        {
            await CreateRoomAsync(MatchMode.TwoVsTwo);
        }

        /// <summary>
        /// Join Code 입력값으로 방에 입장합니다.
        /// 공백 및 대소문자 정규화는 SessionFacade가 처리합니다.
        /// </summary>
        public async void OnJoinRoomPressed()
        {
            if (flowController == null)
            {
                return;
            }

            string code = joinCodeInput != null ? joinCodeInput.text : string.Empty;
            await RunUiOperationAsync(() => flowController.JoinRoomAsync(code));
        }

        private async System.Threading.Tasks.Task CreateRoomAsync(MatchMode mode)
        {
            if (flowController == null)
            {
                return;
            }

            await RunUiOperationAsync(() => flowController.CreateRoomAsync(mode));
        }

        /// <summary>
        /// 버튼 클릭에서 발생한 비동기 작업의 공통 예외 처리입니다.
        /// 정상 실패 메시지는 FlowController가 State와 LastError로 전달합니다.
        /// </summary>
        private async System.Threading.Tasks.Task RunUiOperationAsync(
            Func<System.Threading.Tasks.Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                // Presenter가 예외를 삼키지 않고, 사용자가 확인 가능한 상태 문구를 남깁니다.
                Debug.LogException(exception);
                SetStatus("An unexpected menu operation failed. Check the console.");
            }
        }

        private void OnFlowStateChanged(MultiplayerFlowState state)
        {
            RefreshView(state);
        }

        /// <summary>
        /// Flow 진행 중에는 중복 요청을 막기 위해 Create/Join 컨트롤을 비활성화합니다.
        /// </summary>
        private void RefreshView(MultiplayerFlowState state)
        {
            bool isBusy = state == MultiplayerFlowState.Initializing ||
                          state == MultiplayerFlowState.CreatingRoom ||
                          state == MultiplayerFlowState.JoiningRoom ||
                          state == MultiplayerFlowState.LeavingRoom;

            SetMenuInteractable(!isBusy);

            switch (state)
            {
                case MultiplayerFlowState.Menu:
                    SetStatus("Create a room or enter a join code.");
                    break;

                case MultiplayerFlowState.Initializing:
                    SetStatus("Initializing Unity Services...");
                    break;

                case MultiplayerFlowState.CreatingRoom:
                    SetStatus("Creating room and connecting host...");
                    break;

                case MultiplayerFlowState.JoiningRoom:
                    SetStatus("Joining room and connecting client...");
                    break;

                case MultiplayerFlowState.InRoom:
                    SetStatus("Joined room.");
                    break;

                case MultiplayerFlowState.Error:
                    SetStatus(string.IsNullOrEmpty(flowController.LastError)
                        ? "The room operation failed."
                        : flowController.LastError);
                    break;
            }
        }

        private void SetMenuInteractable(bool interactable)
        {
            if (createOneVsOneButton != null)
            {
                createOneVsOneButton.interactable = interactable;
            }

            if (createTwoVsTwoButton != null)
            {
                createTwoVsTwoButton.interactable = interactable;
            }

            if (joinCodeInput != null)
            {
                joinCodeInput.interactable = interactable;
            }

            if (joinRoomButton != null)
            {
                joinRoomButton.interactable = interactable;
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
