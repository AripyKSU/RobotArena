using System;
using Unity.Collections;
using Unity.Netcode;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// 이번 MVP에서 지원하는 팀 구성 모드입니다.
    /// </summary>
    public enum MatchMode : byte
    {
        OneVsOne,
        TwoVsTwo
    }

    /// <summary>
    /// NGO로 동기화할 방의 진행 단계입니다.
    /// </summary>
    public enum RoomPhase : byte
    {
        Waiting,
        Starting,
        InGame,
        Finished
    }

    /// <summary>
    /// 대기실에서 사용하는 두 팀입니다. 팀 변경은 항상 서버가 승인합니다.
    /// </summary>
    public enum RoomTeam : byte
    {
        TeamA,
        TeamB
    }

    /// <summary>
    /// RoomNetworkState가 모든 참가자에게 복제하는 최소 플레이어 정보입니다.
    /// </summary>
    public struct RoomPlayerState : INetworkSerializable, IEquatable<RoomPlayerState>
    {
        public ulong ClientId;
        public FixedString64Bytes DisplayName;
        public RoomTeam Team;

        public RoomPlayerState(ulong clientId, FixedString64Bytes displayName, RoomTeam team)
        {
            ClientId = clientId;
            DisplayName = displayName;
            Team = team;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref Team);
        }

        public bool Equals(RoomPlayerState other)
        {
            return ClientId == other.ClientId &&
                   DisplayName.Equals(other.DisplayName) &&
                   Team == other.Team;
        }
    }

    /// <summary>
    /// 현재 로컬 플레이어가 Session에서 맡는 역할입니다.
    /// </summary>
    public enum SessionRole : byte
    {
        None,
        Host,
        Client
    }

    /// <summary>
    /// SessionFacade가 현재 수행 중인 Session 작업 상태입니다.
    /// </summary>
    public enum SessionOperationState : byte
    {
        Idle,
        Creating,
        Joining,
        InSession,
        Leaving
    }

    /// <summary>
    /// 이후 MultiplayerFlowController가 관리할 전체 멀티플레이어 흐름 상태입니다.
    /// </summary>
    public enum MultiplayerFlowState : byte
    {
        Offline,
        Initializing,
        Menu,
        CreatingRoom,
        JoiningRoom,
        InRoom,
        LeavingRoom,
        Error
    }

    /// <summary>
    /// 모드별 팀 정원과 전체 Session 정원을 한 곳에서 결정합니다.
    /// </summary>
    public readonly struct MatchModeRules
    {
        public int TeamACapacity { get; }
        public int TeamBCapacity { get; }

        public int MaxPlayers => TeamACapacity + TeamBCapacity;

        private MatchModeRules(int teamACapacity, int teamBCapacity)
        {
            TeamACapacity = teamACapacity;
            TeamBCapacity = teamBCapacity;
        }

        /// <summary>
        /// 지정한 모드의 팀별 정원을 반환합니다.
        /// 새 모드를 추가할 때는 이 매핑만 확장합니다.
        /// </summary>
        public static MatchModeRules For(MatchMode mode)
        {
            return mode switch
            {
                MatchMode.OneVsOne => new MatchModeRules(1, 1),
                MatchMode.TwoVsTwo => new MatchModeRules(2, 2),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }
    }

    /// <summary>
    /// Session SDK 오류를 게임 코드에서 구분하기 위한 간결한 실패 이유입니다.
    /// </summary>
    public enum SessionFailureReason : byte
    {
        None,
        Busy,
        InvalidCode,
        NotFound,
        Full,
        Locked,
        RateLimited,
        AuthenticationFailed,
        ServiceError,
        Unknown
    }

    /// <summary>
    /// Session 작업 결과입니다. UI는 원본 예외 대신 이 값을 사용합니다.
    /// </summary>
    public readonly struct SessionOperationResult
    {
        public bool Success { get; }
        public SessionFailureReason FailureReason { get; }
        public string Message { get; }

        private SessionOperationResult(
            bool success,
            SessionFailureReason failureReason,
            string message)
        {
            Success = success;
            FailureReason = failureReason;
            Message = message;
        }

        public static SessionOperationResult Succeeded(string message)
        {
            return new SessionOperationResult(true, SessionFailureReason.None, message);
        }

        public static SessionOperationResult Failed(
            SessionFailureReason failureReason,
            string message)
        {
            return new SessionOperationResult(false, failureReason, message);
        }
    }
}
