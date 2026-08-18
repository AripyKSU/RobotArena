using System;
using TMPro;
using UnityEngine;

namespace FPSBasic.Multiplayer.UI
{
    public sealed class MatchHUD : MonoBehaviour
    {
        private const int MatchDurationDisplaySeconds = 300;

        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text teamAScoreText;
        [SerializeField] private TMP_Text separatorText;
        [SerializeField] private TMP_Text teamBScoreText;

        private NetworkMatchController _controller;
        private double _localEndsAt;
        private bool _timerRunning;
        private int _lastDisplayedSecond = -1;

        private void OnEnable()
        {
            NetworkMatchController.Spawned += HandleControllerSpawned;
            NetworkMatchController.Despawned += HandleControllerDespawned;

            SetTimerText(MatchDurationDisplaySeconds);
            SetScoreText(0, 0);
            Bind(NetworkMatchController.LocalInstance);
        }

        private void OnDisable()
        {
            NetworkMatchController.Spawned -= HandleControllerSpawned;
            NetworkMatchController.Despawned -= HandleControllerDespawned;
            Bind(null);
        }

        private void Update()
        {
            if (!_timerRunning)
            {
                return;
            }

            double remainingSeconds = Math.Max(0d, _localEndsAt - Time.unscaledTimeAsDouble);
            int displaySeconds = Mathf.CeilToInt((float)remainingSeconds);
            SetTimerText(displaySeconds);

            if (remainingSeconds <= 0d)
            {
                _timerRunning = false;
                SetTimerText(0);
            }
        }

        private void HandleControllerSpawned(NetworkMatchController controller)
        {
            Bind(controller);
        }

        private void HandleControllerDespawned(NetworkMatchController controller)
        {
            if (_controller == controller)
            {
                Bind(null);
            }
        }

        private void Bind(NetworkMatchController controller)
        {
            if (_controller == controller)
            {
                RefreshFromController();
                return;
            }

            if (_controller != null)
            {
                _controller.MatchStarted -= HandleMatchStarted;
                _controller.MatchFinished -= HandleMatchFinished;
                _controller.TeamAScore.OnValueChanged -= HandleTeamAScoreChanged;
                _controller.TeamBScore.OnValueChanged -= HandleTeamBScoreChanged;
            }

            _controller = controller;

            if (_controller != null)
            {
                _controller.MatchStarted += HandleMatchStarted;
                _controller.MatchFinished += HandleMatchFinished;
                _controller.TeamAScore.OnValueChanged += HandleTeamAScoreChanged;
                _controller.TeamBScore.OnValueChanged += HandleTeamBScoreChanged;
            }

            RefreshFromController();
        }

        private void RefreshFromController()
        {
            if (_controller == null || !_controller.IsSpawned)
            {
                return;
            }

            SetScoreText(
                _controller.TeamAScore.Value,
                _controller.TeamBScore.Value);

            if (_controller.Result != MatchResult.None)
            {
                _timerRunning = false;
                SetTimerText(0);
                return;
            }

            double remainingSeconds = _controller.GetRemainingSecondsAtCurrentPeer();
            if (_controller.IsMatchRunning && remainingSeconds > 0d)
            {
                StartLocalCountdown(remainingSeconds);
            }
        }

        private void HandleMatchStarted(double remainingSeconds)
        {
            SetScoreText(0, 0);
            StartLocalCountdown(remainingSeconds);
        }

        private void HandleMatchFinished(
            MatchResult result,
            int finalTeamAScore,
            int finalTeamBScore)
        {
            _timerRunning = false;
            SetTimerText(0);
            SetScoreText(finalTeamAScore, finalTeamBScore);
        }

        private void HandleTeamAScoreChanged(int previousValue, int currentValue)
        {
            SetScoreText(currentValue, _controller != null ? _controller.TeamBScore.Value : 0);
        }

        private void HandleTeamBScoreChanged(int previousValue, int currentValue)
        {
            SetScoreText(_controller != null ? _controller.TeamAScore.Value : 0, currentValue);
        }

        private void StartLocalCountdown(double remainingSeconds)
        {
            double clampedRemaining = Math.Max(0d, remainingSeconds);
            _localEndsAt = Time.unscaledTimeAsDouble + clampedRemaining;
            _timerRunning = clampedRemaining > 0d;
            _lastDisplayedSecond = -1;
            SetTimerText(Mathf.CeilToInt((float)clampedRemaining));
        }

        private void SetTimerText(int totalSeconds)
        {
            int clampedSeconds = Mathf.Max(0, totalSeconds);
            if (_lastDisplayedSecond == clampedSeconds)
            {
                return;
            }

            _lastDisplayedSecond = clampedSeconds;
            if (timerText != null)
            {
                int minutes = clampedSeconds / 60;
                int seconds = clampedSeconds % 60;
                timerText.text = $"{minutes:00}:{seconds:00}";
            }
        }

        private void SetScoreText(int teamAScore, int teamBScore)
        {
            if (teamAScoreText != null)
            {
                teamAScoreText.SetText("{0}", Mathf.Max(0, teamAScore));
            }

            if (separatorText != null)
            {
                separatorText.text = ":";
            }

            if (teamBScoreText != null)
            {
                teamBScoreText.SetText("{0}", Mathf.Max(0, teamBScore));
            }
        }
    }
}
