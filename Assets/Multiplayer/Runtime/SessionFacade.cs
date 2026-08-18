using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// 프로젝트에서 UGS Sessions API를 사용하는 유일한 창구입니다.
    /// 방 생성·입장·잠금·퇴장만 담당하며 NGO를 직접 시작하거나 종료하지 않습니다.
    /// </summary>
    public sealed class SessionFacade : MonoBehaviour
    {
        private const string SessionTypePrefix = "fpsbasic-mvp";
        private const string ModePropertyKey = "mode";
        private const string BuildPropertyKey = "build";

        [SerializeField]
        private UgsBootstrap ugsBootstrap;

        // 현재 참가 중인 UGS Session 핸들입니다. UI에 직접 노출하지 않습니다.
        private ISession _currentSession;

        // Create / Join / Lock / Leave 요청이 동시에 실행되는 것을 막습니다.
        private bool _operationInProgress;

        public SessionRole Role { get; private set; } = SessionRole.None;

        public SessionOperationState State { get; private set; } =
            SessionOperationState.Idle;

        public bool HasSession => _currentSession != null;

        public bool IsHost => Role == SessionRole.Host;

        public string SessionId => _currentSession?.Id ?? string.Empty;

        public string JoinCode => _currentSession?.Code ?? string.Empty;

        // 마지막 작업 실패 메시지를 UI 또는 상위 흐름에서 읽을 수 있게 보관합니다.
        public string LastError { get; private set; } = string.Empty;

        // 세션의 멤버, 속성, 삭제 상태가 변경되었음을 상위 흐름에 알립니다.
        public event Action SessionChanged;

        // 원본 예외 대신 UI에 사용할 수 있는 간결한 실패 메시지를 전달합니다.
        public event Action<string> OperationFailed;

        private void Awake()
        {
            if (ugsBootstrap == null)
            {
                Debug.LogError(
                    "[Session] UgsBootstrap reference is missing. " +
                    "Assign it on MultiplayerBootstrap.");
            }
        }

        private void OnDestroy()
        {
            // 현재 Session에 연결한 SDK 이벤트를 누수 없이 해제합니다.
            DetachCurrentSession();
        }

        /// <summary>
        /// 선택한 모드의 정원으로 비공개 Relay Session을 생성합니다.
        /// </summary>
        public async Task<SessionOperationResult> CreateSessionAsync(MatchMode mode)
        {
            if (!TryBeginOperation(SessionOperationState.Creating, out var rejection))
            {
                return rejection;
            }

            try
            {
                // Session API를 호출하기 전에 UGS 초기화와 인증을 보장합니다.
                var ugsResult = await EnsureUgsReadyAsync();
                if (!ugsResult.Success)
                {
                    return ugsResult;
                }

                // 모드별 최대 인원 계산은 중앙 규칙 객체에만 둡니다.
                var rules = MatchModeRules.For(mode);

                Debug.Log(
                    $"[Session] Creating private {mode} session. " +
                    $"MaxPlayers={rules.MaxPlayers}");

                var options = new SessionOptions
                {
                    // Type is only a client-side registration key in the Sessions SDK.
                    // A stable value can remain occupied when an externally deleted session
                    // fails to finish the SDK's asynchronous cleanup, blocking every later
                    // Create/Join with SessionTypeAlreadyExists.
                    Type = CreateLocalSessionType(),
                    Name = $"fps-{Guid.NewGuid():N}",
                    MaxPlayers = rules.MaxPlayers,
                    IsPrivate = true,
                    IsLocked = false,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        // 방 멤버가 읽을 수 있는 저빈도 메타데이터입니다.
                        [ModePropertyKey] = new SessionProperty(
                            GetModePropertyValue(mode),
                            VisibilityPropertyOptions.Member),
                        [BuildPropertyKey] = new SessionProperty(
                            Application.version,
                            VisibilityPropertyOptions.Member)
                    }
                }.WithRelayNetwork();

                // Sessions 네트워크 모듈이 Relay 및 NGO 연결 시작을 소유합니다.
                IHostSession hostSession =
                    await MultiplayerService.Instance.CreateSessionAsync(options);

                AttachCurrentSession(hostSession);
                Role = SessionRole.Host;
                State = SessionOperationState.InSession;

                Debug.Log(
                    $"[Session] Host session created. " +
                    $"SessionId={hostSession.Id} Code={hostSession.Code}");

                SessionChanged?.Invoke();
                return SessionOperationResult.Succeeded("Room created.");
            }
            catch (Exception exception)
            {
                return HandleOperationException("Create session", exception);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 플레이어가 입력한 Join Code로 기존 Relay Session에 참가합니다.
        /// </summary>
        public async Task<SessionOperationResult> JoinSessionByCodeAsync(string joinCode)
        {
            // SDK 호출 전에 공백을 제거하고 대문자 코드로 정규화합니다.
            string normalizedCode = joinCode?.Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(normalizedCode))
            {
                return Fail(SessionFailureReason.InvalidCode, "Join code is required.");
            }

            if (!TryBeginOperation(SessionOperationState.Joining, out var rejection))
            {
                return rejection;
            }

            try
            {
                var ugsResult = await EnsureUgsReadyAsync();
                if (!ugsResult.Success)
                {
                    return ugsResult;
                }

                Debug.Log($"[Session] Joining by code={normalizedCode}");

                var joinOptions = new JoinSessionOptions
                {
                    // This key is local to this client; it does not have to match the host.
                    Type = CreateLocalSessionType()
                };

                ISession session = await MultiplayerService.Instance
                    .JoinSessionByCodeAsync(normalizedCode, joinOptions);

                AttachCurrentSession(session);
                Role = SessionRole.Client;
                State = SessionOperationState.InSession;

                Debug.Log($"[Session] Join succeeded. SessionId={session.Id}");

                SessionChanged?.Invoke();
                return SessionOperationResult.Succeeded("Joined room.");
            }
            catch (Exception exception)
            {
                return HandleOperationException("Join session", exception);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 호스트가 이후 참가를 막기 위해 Session을 잠급니다.
        /// 실제 호출 시점은 미래의 Ready 및 게임 시작 흐름이 결정합니다.
        /// </summary>
        public async Task<SessionOperationResult> LockSessionAsync()
        {
            if (!TryBeginOperation(SessionOperationState.InSession, out var rejection))
            {
                return rejection;
            }

            try
            {
                if (!HasSession || !IsHost)
                {
                    return Fail(
                        SessionFailureReason.ServiceError,
                        "Only the host can lock an active room.");
                }

                // Host 핸들에 잠금 값을 설정한 뒤 서버에 저장합니다.
                IHostSession hostSession = _currentSession.AsHost();
                hostSession.IsLocked = true;
                await hostSession.SavePropertiesAsync();

                Debug.Log($"[Session] Session locked. SessionId={hostSession.Id}");
                SessionChanged?.Invoke();

                return SessionOperationResult.Succeeded("Room locked.");
            }
            catch (Exception exception)
            {
                return HandleOperationException("Lock session", exception);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// 클라이언트는 Leave, 호스트는 Delete로 MVP 방을 종료합니다.
        /// </summary>
        public async Task<SessionOperationResult> LeaveSessionAsync()
        {
            if (!TryBeginOperation(SessionOperationState.Leaving, out var rejection))
            {
                return rejection;
            }

            if (!HasSession)
            {
                EndOperation();
                return SessionOperationResult.Succeeded("No active room.");
            }

            try
            {
                if (IsHost)
                {
                    Debug.Log("[Session] Host deleting session");
                    await _currentSession.AsHost().DeleteAsync();
                }
                else
                {
                    Debug.Log("[Session] Client leaving session");
                    await _currentSession.LeaveAsync();
                }

                return SessionOperationResult.Succeeded("Left room.");
            }
            catch (Exception exception)
            {
                return HandleOperationException("Leave session", exception);
            }
            finally
            {
                // 정상 퇴장과 실패 모두 Session 핸들과 SDK 이벤트를 정리합니다.
                // UgsBootstrap의 인증 상태는 유지하므로 여기서 로그아웃하지 않습니다.
                DetachCurrentSession();
                Role = SessionRole.None;
                State = SessionOperationState.Idle;
                _operationInProgress = false;

                SessionChanged?.Invoke();
            }
        }

        /// <summary>
        /// 현재 UGS Session 객체에서 전달하는 변경 이벤트를 구독합니다.
        /// </summary>
        private void AttachCurrentSession(ISession session)
        {
            DetachCurrentSession();

            _currentSession = session;
            _currentSession.Changed += OnSdkSessionChanged;
            _currentSession.Deleted += OnSdkSessionDeleted;
            _currentSession.RemovedFromSession += OnRemovedFromSdkSession;
        }

        /// <summary>
        /// 현재 Session 핸들에 연결한 모든 SDK 이벤트를 해제합니다.
        /// </summary>
        private void DetachCurrentSession()
        {
            if (_currentSession == null)
            {
                return;
            }

            _currentSession.Changed -= OnSdkSessionChanged;
            _currentSession.Deleted -= OnSdkSessionDeleted;
            _currentSession.RemovedFromSession -= OnRemovedFromSdkSession;
            _currentSession = null;
        }

        private void OnSdkSessionChanged()
        {
            Debug.Log($"[Session] Session changed. SessionId={SessionId}");
            SessionChanged?.Invoke();
        }

        private void OnSdkSessionDeleted()
        {
            Debug.Log("[Session] Session was deleted.");
            ClearSessionAfterExternalEnd();
        }

        private void OnRemovedFromSdkSession()
        {
            Debug.Log("[Session] Local player was removed from the session.");
            ClearSessionAfterExternalEnd();
        }

        /// <summary>
        /// 방장의 Delete 또는 서버 측 강제 퇴장처럼 로컬 Leave 호출 밖에서 세션이 끝난 경우에도
        /// 다음 Create/Join 요청이 오래된 Session 핸들에 막히지 않도록 즉시 정리합니다.
        /// </summary>
        private void ClearSessionAfterExternalEnd()
        {
            DetachCurrentSession();
            Role = SessionRole.None;
            State = SessionOperationState.Idle;
            _operationInProgress = false;
            SessionChanged?.Invoke();
        }

        /// <summary>
        /// 동시에 하나의 Session SDK 요청만 실행되도록 보장합니다.
        /// </summary>
        private bool TryBeginOperation(
            SessionOperationState nextState,
            out SessionOperationResult result)
        {
            if (_operationInProgress)
            {
                result = Fail(
                    SessionFailureReason.Busy,
                    "Another session operation is already in progress.");
                return false;
            }

            if (HasSession && nextState != SessionOperationState.Leaving &&
                nextState != SessionOperationState.InSession)
            {
                result = Fail(
                    SessionFailureReason.Busy,
                    "Already in a session. Leave it before creating or joining another.");
                return false;
            }

            _operationInProgress = true;
            State = nextState;
            LastError = string.Empty;

            result = default;
            return true;
        }

        /// <summary>
        /// Session 작업 전에 UGS 초기화와 익명 인증이 준비되었는지 확인합니다.
        /// </summary>
        private async Task<SessionOperationResult> EnsureUgsReadyAsync()
        {
            if (ugsBootstrap == null)
            {
                return Fail(
                    SessionFailureReason.AuthenticationFailed,
                    "UGS bootstrap is not configured.");
            }

            try
            {
                await ugsBootstrap.EnsureReadyAsync();
                return SessionOperationResult.Succeeded("UGS is ready.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);

                return Fail(
                    SessionFailureReason.AuthenticationFailed,
                    "Unable to initialize Unity Services authentication.");
            }
        }

        // 작업 종료 후 Session이 있으면 InSession, 없으면 Idle 상태로 되돌립니다.
        private void EndOperation()
        {
            _operationInProgress = false;
            State = HasSession
                ? SessionOperationState.InSession
                : SessionOperationState.Idle;
        }

        /// <summary>
        /// Unity SessionException을 프로젝트의 실패 이유와 메시지로 변환합니다.
        /// </summary>
        private SessionOperationResult HandleOperationException(
            string operationName,
            Exception exception)
        {
            Debug.LogError($"[Session] {operationName} failed: {exception.Message}");
            Debug.LogException(exception);

            SessionFailureReason reason = MapFailureReason(exception);
            return Fail(reason, GetFailureMessage(reason));
        }

        private SessionOperationResult Fail(
            SessionFailureReason reason,
            string message)
        {
            LastError = message;

            Debug.LogWarning($"[Session] {reason}: {message}");
            OperationFailed?.Invoke(message);

            return SessionOperationResult.Failed(reason, message);
        }

        private static SessionFailureReason MapFailureReason(Exception exception)
        {
            if (exception is SessionException sessionException)
            {
                switch (sessionException.Error)
                {
                    case SessionError.SessionNotFound:
                    case SessionError.SessionDeleted:
                        return SessionFailureReason.NotFound;

                    case SessionError.RateLimitExceeded:
                        return SessionFailureReason.RateLimited;

                    case SessionError.NotAuthorized:
                        return SessionFailureReason.AuthenticationFailed;

                    case SessionError.LobbyAlreadyExists:
                    case SessionError.SessionTypeAlreadyExists:
                    case SessionError.SessionConflict:
                        return SessionFailureReason.Busy;
                }
            }

            // SDK 2.3.0에는 Full/Locked 전용 SessionError가 없으므로 메시지를 보조 분류합니다.
            string message = exception.Message?.ToLowerInvariant() ?? string.Empty;

            if (message.Contains("locked"))
            {
                return SessionFailureReason.Locked;
            }

            if (message.Contains("full"))
            {
                return SessionFailureReason.Full;
            }

            return SessionFailureReason.Unknown;
        }

        private static string GetFailureMessage(SessionFailureReason reason)
        {
            return reason switch
            {
                SessionFailureReason.NotFound => "Room not found.",
                SessionFailureReason.Locked => "This room is locked.",
                SessionFailureReason.Full => "This room is full.",
                SessionFailureReason.RateLimited =>
                    "Too many requests. Please try again shortly.",
                SessionFailureReason.AuthenticationFailed =>
                    "Unity Services authentication is unavailable.",
                SessionFailureReason.Busy =>
                    "Another room operation is already in progress.",
                _ => "The room operation failed. Please try again."
            };
        }

        // Session 속성에 저장할 안정적인 모드 문자열입니다.
        private static string GetModePropertyValue(MatchMode mode)
        {
            return mode switch
            {
                MatchMode.OneVsOne => "1v1",
                MatchMode.TwoVsTwo => "2v2",
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }

        private static string CreateLocalSessionType()
        {
            return $"{SessionTypePrefix}-{Guid.NewGuid():N}";
        }
    }
}
