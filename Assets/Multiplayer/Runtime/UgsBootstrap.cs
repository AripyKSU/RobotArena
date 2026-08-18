using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    public enum UgsInitializationState : byte
    {
        Uninitialized,
        Initializing,
        Ready,
        Failed
    }

    /// <summary>
    /// Unity Gaming Services와 익명 인증을 한 번만 초기화하는 부트스트랩입니다.
    /// 세션·Relay·NGO 연결 상태는 이 클래스가 관리하지 않습니다.
    /// </summary>
    public sealed class UgsBootstrap : MonoBehaviour
    {
        // 여러 호출자가 초기화를 요청해도 하나의 비동기 작업을 공유하기 위한 Task입니다.
        private Task _initializationTask;

        // 인증 성공 후 보관하는 로컬 플레이어 ID입니다.
        private string _playerId = string.Empty;

        public UgsInitializationState State { get; private set; } =
            UgsInitializationState.Uninitialized;

        public bool IsReady => State == UgsInitializationState.Ready;

        public string PlayerId => _playerId;

        // 실패 원인을 이후 로그 또는 UI 계층에서 읽을 수 있도록 보관합니다.
        public string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// UGS 초기화와 익명 로그인을 보장합니다.
        /// 이미 진행 중인 초기화가 있으면 새 요청을 만들지 않고 같은 Task를 반환합니다.
        /// </summary>
        public Task EnsureReadyAsync()
        {
            // 준비가 끝난 뒤의 호출은 즉시 성공합니다.
            if (State == UgsInitializationState.Ready)
            {
                return Task.CompletedTask;
            }

            // 초기화 중이거나 이미 실패한 작업이 있으면 그대로 공유합니다.
            // 이로써 버튼 연타 등으로 중복 SDK 요청이 발생하지 않습니다.
            if (_initializationTask != null)
            {
                return _initializationTask;
            }

            _initializationTask = InitializeAndSignInAsync();
            return _initializationTask;
        }

        /// <summary>
        /// Unity Services 초기화 후, 필요할 때만 익명 인증을 수행합니다.
        /// </summary>
        private async Task InitializeAndSignInAsync()
        {
            State = UgsInitializationState.Initializing;
            LastError = string.Empty;

            try
            {
                // 모든 UGS API 사용 전에 Core 서비스를 초기화합니다.
                Debug.Log("[UGS] Initialization started");
                await UnityServices.InitializeAsync();

                // 기존 인증이 없다면 이 MVP의 기본 인증 방식인 익명 로그인으로 계정을 만듭니다.
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                    Debug.Log(
                        $"[UGS] Anonymous sign-in succeeded. " +
                        $"PlayerId={AuthenticationService.Instance.PlayerId}");
                }
                else
                {
                    // 세션 연결이 끊겨도 인증은 유지되므로, 기존 인증을 재사용합니다.
                    Debug.Log(
                        $"[UGS] Authentication already active. " +
                        $"PlayerId={AuthenticationService.Instance.PlayerId}");
                }

                // 초기화와 인증이 모두 성공한 경우에만 Ready 상태로 전환합니다.
                _playerId = AuthenticationService.Instance.PlayerId;
                State = UgsInitializationState.Ready;
            }
            catch (Exception exception)
            {
                // 오류 원문은 콘솔에 남기고, 이후 계층에는 간결한 메시지만 제공합니다.
                State = UgsInitializationState.Failed;
                LastError = exception.Message;

                Debug.LogError($"[UGS] Initialization failed: {LastError}");
                Debug.LogException(exception);

                throw;
            }
        }
    }
}
