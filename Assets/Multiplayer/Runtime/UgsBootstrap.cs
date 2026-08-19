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
        private const string DefaultProfile = "default";
        private const string TestProfileQueryKey = "ugsProfile";
        private const string TestProfileCommandLinePrefix = "-ugsProfile=";
        private const int MaxProfileLength = 30;

        // 여러 호출자가 초기화를 요청해도 하나의 비동기 작업을 공유하기 위한 Task입니다.
        private Task _initializationTask;

        // 인증 성공 후 보관하는 로컬 플레이어 ID와 실제 UGS 프로필입니다.
        private string _playerId = string.Empty;
        private string _profile = DefaultProfile;

        public UgsInitializationState State { get; private set; } =
            UgsInitializationState.Uninitialized;

        public bool IsReady => State == UgsInitializationState.Ready;

        public string PlayerId => _playerId;

        public string Profile => _profile;

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
        /// 운영 WebGL은 default 프로필을 사용하므로 브라우저 IndexedDB별 PlayerId가 유지됩니다.
        /// Development Build와 Editor에서만 URL/명령줄의 ugsProfile 테스트 재정의를 허용합니다.
        /// </summary>
        private async Task InitializeAndSignInAsync()
        {
            State = UgsInitializationState.Initializing;
            LastError = string.Empty;

            try
            {
                var initializationOptions = new InitializationOptions();
                string requestedTestProfile = ResolveRequestedTestProfile();

                if (!string.IsNullOrEmpty(requestedTestProfile))
                {
                    initializationOptions.SetProfile(requestedTestProfile);
                }

                // 모든 UGS API 사용 전에 Core 서비스를 초기화합니다.
                Debug.Log(
                    string.IsNullOrEmpty(requestedTestProfile)
                        ? "[UGS] Initialization started. Profile=default"
                        : $"[UGS] Initialization started. TestProfile={requestedTestProfile}");

                await UnityServices.InitializeAsync(initializationOptions);
                _profile = AuthenticationService.Instance.Profile;

                // 캐시된 세션 토큰이 있으면 기존 익명 사용자를 복구하고,
                // 없으면 현재 브라우저 저장소를 위한 새 PlayerId를 생성합니다.
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                    Debug.Log(
                        $"[UGS] Anonymous sign-in succeeded. " +
                        $"Profile={_profile} PlayerId={AuthenticationService.Instance.PlayerId}");
                }
                else
                {
                    // 세션 연결이 끊겨도 인증은 유지되므로, 기존 인증을 재사용합니다.
                    Debug.Log(
                        $"[UGS] Authentication already active. " +
                        $"Profile={_profile} PlayerId={AuthenticationService.Instance.PlayerId}");
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

        private static string ResolveRequestedTestProfile()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string urlProfile = GetQueryParameter(
                Application.absoluteURL,
                TestProfileQueryKey);

            if (IsValidProfile(urlProfile))
            {
                return urlProfile;
            }

            if (!string.IsNullOrEmpty(urlProfile))
            {
                Debug.LogWarning(
                    $"[UGS] Ignoring invalid URL test profile '{urlProfile}'. " +
                    "Use 1-30 letters, numbers, '-' or '_'.");
            }

            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (!argument.StartsWith(
                        TestProfileCommandLinePrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string commandLineProfile =
                    argument.Substring(TestProfileCommandLinePrefix.Length);

                if (IsValidProfile(commandLineProfile))
                {
                    return commandLineProfile;
                }

                Debug.LogWarning(
                    $"[UGS] Ignoring invalid command-line test profile " +
                    $"'{commandLineProfile}'. Use 1-30 letters, numbers, '-' or '_'.");
                break;
            }
#endif

            return string.Empty;
        }

        private static string GetQueryParameter(string url, string key)
        {
            if (string.IsNullOrEmpty(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out Uri parsedUri))
            {
                return string.Empty;
            }

            string query = parsedUri.Query;
            if (string.IsNullOrEmpty(query))
            {
                return string.Empty;
            }

            string[] entries = query.TrimStart('?').Split('&');
            foreach (string entry in entries)
            {
                int separatorIndex = entry.IndexOf('=');
                string encodedKey = separatorIndex >= 0
                    ? entry.Substring(0, separatorIndex)
                    : entry;

                if (!string.Equals(
                        DecodeQueryValue(encodedKey),
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string encodedValue = separatorIndex >= 0
                    ? entry.Substring(separatorIndex + 1)
                    : string.Empty;
                return DecodeQueryValue(encodedValue);
            }

            return string.Empty;
        }

        private static string DecodeQueryValue(string value)
        {
            return Uri.UnescapeDataString(value.Replace("+", " "));
        }

        private static bool IsValidProfile(string profile)
        {
            if (string.IsNullOrEmpty(profile) || profile.Length > MaxProfileLength)
            {
                return false;
            }

            foreach (char character in profile)
            {
                bool isAllowed =
                    character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' ||
                    character == '-' ||
                    character == '_';

                if (!isAllowed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
