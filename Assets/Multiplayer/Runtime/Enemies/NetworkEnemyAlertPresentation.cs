using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer.Enemies
{
    /// <summary>Replicates the server AI's detection presentation to remote clients.</summary>
    [RequireComponent(typeof(EnemyController), typeof(EnemyMobile), typeof(NetworkEnemyLifecycle))]
    public sealed class NetworkEnemyAlertPresentation : NetworkBehaviour
    {
        private const string AlertedAnimatorParameter = "Alerted";

        public readonly NetworkVariable<bool> IsAlerted = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private EnemyController _enemyController;
        private EnemyMobile _enemyMobile;
        private NetworkEnemyLifecycle _lifecycle;

        private void Awake()
        {
            _enemyController = GetComponent<EnemyController>();
            _enemyMobile = GetComponent<EnemyMobile>();
            _lifecycle = GetComponent<NetworkEnemyLifecycle>();
        }

        public override void OnNetworkSpawn()
        {
            IsAlerted.OnValueChanged += HandleAlertedChanged;

            if (IsServer)
            {
                _enemyController.onDetectedTarget += HandleServerDetectedTarget;
                _enemyController.onLostTarget += HandleServerLostTarget;
                _lifecycle.IsCombatActive.OnValueChanged += HandleCombatActiveChanged;
            }
            else
            {
                ApplyAlertPresentation(IsAlerted.Value, false);
            }
        }

        public override void OnNetworkDespawn()
        {
            IsAlerted.OnValueChanged -= HandleAlertedChanged;

            if (IsServer)
            {
                _enemyController.onDetectedTarget -= HandleServerDetectedTarget;
                _enemyController.onLostTarget -= HandleServerLostTarget;
                _lifecycle.IsCombatActive.OnValueChanged -= HandleCombatActiveChanged;
            }

            if (!IsServer)
            {
                ApplyAlertPresentation(false, false);
            }
        }

        private void HandleServerDetectedTarget()
        {
            IsAlerted.Value = true;
        }

        private void HandleServerLostTarget()
        {
            IsAlerted.Value = false;
        }

        private void HandleCombatActiveChanged(bool previousValue, bool currentValue)
        {
            if (IsServer && !currentValue)
            {
                IsAlerted.Value = false;
            }
        }

        private void HandleAlertedChanged(bool previousValue, bool currentValue)
        {
            // EnemyMobile already presents the authoritative state on the host/server.
            if (!IsServer)
            {
                ApplyAlertPresentation(currentValue, currentValue && !previousValue);
            }
        }

        private void ApplyAlertPresentation(bool alerted, bool playDetectionSound)
        {
            if (_enemyMobile == null)
            {
                return;
            }

            if (_enemyMobile.OnDetectVfx != null)
            {
                foreach (ParticleSystem effect in _enemyMobile.OnDetectVfx)
                {
                    if (effect == null)
                    {
                        continue;
                    }

                    if (alerted)
                    {
                        effect.Play(true);
                    }
                    else
                    {
                        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }

            if (_enemyMobile.Animator != null)
            {
                _enemyMobile.Animator.SetBool(AlertedAnimatorParameter, alerted);
            }

            if (playDetectionSound && _enemyMobile.OnDetectSfx != null)
            {
                AudioUtility.CreateSFX(
                    _enemyMobile.OnDetectSfx,
                    transform.position,
                    AudioUtility.AudioGroups.EnemyDetection,
                    1f);
            }
        }
    }
}
