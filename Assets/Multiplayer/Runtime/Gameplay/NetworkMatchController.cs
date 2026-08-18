using System;
using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    public enum MatchResult : byte
    {
        None,
        TeamAWin,
        TeamBWin,
        Draw
    }

    [RequireComponent(typeof(RoomNetworkState))]
    public sealed class NetworkMatchController : NetworkBehaviour
    {
        public const double DefaultMatchDurationSeconds = 300d;

        private readonly NetworkVariable<int> _teamAScore = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _teamBScore = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<double> _serverEndsAt = new(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<MatchResult> _result = new(
            MatchResult.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _finalTeamAScore = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _finalTeamBScore = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [SerializeField, Min(1f)] private double matchDurationSeconds = DefaultMatchDurationSeconds;

        private RoomNetworkState _roomState;

        public static NetworkMatchController LocalInstance { get; private set; }
        public static event Action<NetworkMatchController> Spawned;
        public static event Action<NetworkMatchController> Despawned;
        public static event Action<MatchResult, int, int> LocalMatchFinished;

        public event Action<double> MatchStarted;
        public event Action<MatchResult, int, int> MatchFinished;

        public NetworkVariable<int> TeamAScore => _teamAScore;
        public NetworkVariable<int> TeamBScore => _teamBScore;
        public double ServerEndsAt => _serverEndsAt.Value;
        public MatchResult Result => _result.Value;
        public int FinalTeamAScore => _finalTeamAScore.Value;
        public int FinalTeamBScore => _finalTeamBScore.Value;
        public bool IsMatchRunning =>
            IsSpawned &&
            _roomState != null &&
            _roomState.Phase.Value == RoomPhase.InGame &&
            _result.Value == MatchResult.None;

        private void Awake()
        {
            _roomState = GetComponent<RoomNetworkState>();
        }

        public override void OnNetworkSpawn()
        {
            LocalInstance = this;
            Spawned?.Invoke(this);

            if (IsServer)
            {
                ServerGameplayReadySignal.Raised += HandleServerGameplayReady;
                if (ServerGameplayReadySignal.IsRaised)
                {
                    HandleServerGameplayReady();
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ServerGameplayReadySignal.Raised -= HandleServerGameplayReady;
            }

            if (LocalInstance == this)
            {
                LocalInstance = null;
            }

            Despawned?.Invoke(this);
        }

        private void Update()
        {
            if (!IsServer || !IsMatchRunning)
            {
                return;
            }

            if (NetworkManager.ServerTime.Time >= _serverEndsAt.Value)
            {
                FinishMatchServer();
            }
        }

        public double GetRemainingSecondsAtCurrentPeer()
        {
            if (!IsSpawned || NetworkManager == null || _serverEndsAt.Value <= 0d)
            {
                return 0d;
            }

            return Math.Max(0d, _serverEndsAt.Value - NetworkManager.ServerTime.Time);
        }

        public bool TryRegisterPlayerKill(ulong killerClientId, ulong victimClientId)
        {
            if (!IsServer || !IsMatchRunning)
            {
                return false;
            }

            if (NetworkManager.ServerTime.Time >= _serverEndsAt.Value)
            {
                FinishMatchServer();
                return false;
            }

            if (killerClientId == victimClientId ||
                !TryGetPlayerTeam(killerClientId, out RoomTeam killerTeam) ||
                !TryGetPlayerTeam(victimClientId, out RoomTeam victimTeam) ||
                killerTeam == victimTeam)
            {
                return false;
            }

            if (killerTeam == RoomTeam.TeamA)
            {
                _teamAScore.Value++;
            }
            else
            {
                _teamBScore.Value++;
            }

            return true;
        }

        public static MatchResult DetermineResult(int teamAScore, int teamBScore)
        {
            if (teamAScore > teamBScore)
            {
                return MatchResult.TeamAWin;
            }

            if (teamBScore > teamAScore)
            {
                return MatchResult.TeamBWin;
            }

            return MatchResult.Draw;
        }

        private void HandleServerGameplayReady()
        {
            if (!IsServer ||
                _roomState == null ||
                _roomState.Phase.Value != RoomPhase.Starting)
            {
                return;
            }

            _teamAScore.Value = 0;
            _teamBScore.Value = 0;
            _finalTeamAScore.Value = 0;
            _finalTeamBScore.Value = 0;
            _result.Value = MatchResult.None;
            _serverEndsAt.Value = NetworkManager.ServerTime.Time + matchDurationSeconds;
            _roomState.Phase.Value = RoomPhase.InGame;

            MatchStartedClientRpc(_serverEndsAt.Value);
            Debug.Log($"[Match] Started. Duration={matchDurationSeconds:0.###}s EndsAt={_serverEndsAt.Value:0.###}");
        }

        private void FinishMatchServer()
        {
            if (!IsServer || _roomState == null || _roomState.Phase.Value != RoomPhase.InGame)
            {
                return;
            }

            _finalTeamAScore.Value = _teamAScore.Value;
            _finalTeamBScore.Value = _teamBScore.Value;
            _result.Value = DetermineResult(_finalTeamAScore.Value, _finalTeamBScore.Value);
            _roomState.Phase.Value = RoomPhase.Finished;

            MatchFinishedClientRpc(
                _result.Value,
                _finalTeamAScore.Value,
                _finalTeamBScore.Value);

            Debug.Log(
                $"[Match] Finished. Result={_result.Value} " +
                $"Score={_finalTeamAScore.Value}:{_finalTeamBScore.Value}");
        }

        private bool TryGetPlayerTeam(ulong clientId, out RoomTeam team)
        {
            if (_roomState != null)
            {
                foreach (RoomPlayerState player in _roomState.Players)
                {
                    if (player.ClientId == clientId)
                    {
                        team = player.Team;
                        return true;
                    }
                }
            }

            team = default;
            return false;
        }

        [ClientRpc]
        private void MatchStartedClientRpc(double serverEndsAt)
        {
            double remainingSeconds = NetworkManager == null
                ? matchDurationSeconds
                : Math.Max(0d, serverEndsAt - NetworkManager.ServerTime.Time);

            MatchStarted?.Invoke(remainingSeconds);
        }

        [ClientRpc]
        private void MatchFinishedClientRpc(
            MatchResult result,
            int finalTeamAScore,
            int finalTeamBScore)
        {
            MatchFinished?.Invoke(result, finalTeamAScore, finalTeamBScore);
            LocalMatchFinished?.Invoke(result, finalTeamAScore, finalTeamBScore);
        }
    }
}
