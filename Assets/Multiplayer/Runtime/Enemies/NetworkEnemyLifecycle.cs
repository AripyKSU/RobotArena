using System;
using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace FPSBasic.Multiplayer.Enemies
{
    /// <summary>Synchronizes pooled enemy health, activity, death and attacker attribution.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(Health), typeof(NetworkEnemyAuthority))]
    public sealed class NetworkEnemyLifecycle : NetworkBehaviour
    {
        public static event Action<NetworkEnemyLifecycle, ulong> AnyServerKilledByPlayer;

        public readonly NetworkVariable<float> CurrentHealth = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> IsCombatActive = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<double> RespawnAtServerTime = new(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public event Action<NetworkEnemyLifecycle, ulong> ServerKilledByPlayer;

        private Health _health;
        private EnemyController _enemyController;
        private EnemyMobile _enemyMobile;
        private NetworkEnemyAuthority _authority;
        private NetworkTransform _networkTransform;
        private NavMeshAgent _navMeshAgent;
        private Renderer[] _renderers;
        private Collider[] _colliders;
        private Canvas[] _canvases;
        private EnemySpawnPoint _ownerSpawnPoint;
        private ulong? _lastValidAttackerClientId;
        private bool _deathHandled;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _enemyController = GetComponent<EnemyController>();
            _enemyMobile = GetComponent<EnemyMobile>();
            _authority = GetComponent<NetworkEnemyAuthority>();
            _networkTransform = GetComponent<NetworkTransform>();
            _navMeshAgent = GetComponent<NavMeshAgent>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            _canvases = GetComponentsInChildren<Canvas>(true);

            if (_enemyController != null)
            {
                _enemyController.ExternalDeathHandling = true;
            }
        }

        public override void OnNetworkSpawn()
        {
            CurrentHealth.OnValueChanged += HandleHealthChanged;
            IsCombatActive.OnValueChanged += HandleActiveChanged;

            if (IsServer)
            {
                _health.OnDamaged += HandleServerDamaged;
                _health.OnDie += HandleServerDeath;
            }
            else
            {
                _health.ApplyNetworkState(CurrentHealth.Value);
            }

            ApplyCombatState(IsCombatActive.Value);
        }

        public override void OnNetworkDespawn()
        {
            CurrentHealth.OnValueChanged -= HandleHealthChanged;
            IsCombatActive.OnValueChanged -= HandleActiveChanged;

            if (IsServer)
            {
                _health.OnDamaged -= HandleServerDamaged;
                _health.OnDie -= HandleServerDeath;
            }
        }

        public void ServerBind(EnemySpawnPoint spawnPoint)
        {
            _ownerSpawnPoint = spawnPoint;
        }

        public void ServerActivate(Vector3 position, Quaternion rotation)
        {
            if (!IsServer)
            {
                return;
            }

            _deathHandled = false;
            _lastValidAttackerClientId = null;
            _health.Invincible = true;
            IsCombatActive.Value = false;
            ApplyCombatState(false);

            transform.SetPositionAndRotation(position, rotation);
            if (_networkTransform != null && _networkTransform.IsSpawned)
            {
                _networkTransform.Teleport(position, rotation, transform.localScale);
            }

            _health.Revive();
            _enemyController?.ResetForReuse();
            _enemyMobile?.ResetForReuse();

            CurrentHealth.Value = _health.CurrentHealth;
            RespawnAtServerTime.Value = 0d;
            IsCombatActive.Value = true;
            _health.Invincible = false;
            ApplyCombatState(true);
        }

        public bool TryGetLastValidAttacker(out ulong clientId)
        {
            if (_lastValidAttackerClientId.HasValue)
            {
                clientId = _lastValidAttackerClientId.Value;
                return true;
            }

            clientId = default;
            return false;
        }

        private void HandleServerDamaged(float damage, GameObject damageSource)
        {
            if (!IsServer || !IsCombatActive.Value || damage <= 0f)
            {
                return;
            }

            CurrentHealth.Value = _health.CurrentHealth;
            if (TryResolvePlayerClientId(damageSource, out ulong clientId))
            {
                _lastValidAttackerClientId = clientId;
            }
        }

        private void HandleServerDeath()
        {
            if (!IsServer || _deathHandled || !IsCombatActive.Value)
            {
                return;
            }

            _deathHandled = true;
            _health.Invincible = true;
            CurrentHealth.Value = 0f;
            IsCombatActive.Value = false;
            RespawnAtServerTime.Value = NetworkManager.ServerTime.Time +
                                        (_ownerSpawnPoint != null ? _ownerSpawnPoint.RespawnDelay : 10f);
            ApplyCombatState(false);
            PlayDeathEffectsClientRpc(transform.position);

            if (_lastValidAttackerClientId.HasValue)
            {
                ulong killerClientId = _lastValidAttackerClientId.Value;
                ServerKilledByPlayer?.Invoke(this, killerClientId);
                AnyServerKilledByPlayer?.Invoke(this, killerClientId);
            }

            if (_ownerSpawnPoint != null)
            {
                _ownerSpawnPoint.ServerScheduleRespawn(this);
            }
            else
            {
                Debug.LogError($"[EnemySpawn] {name} died without an owning spawn point.", this);
            }
        }

        private void HandleHealthChanged(float previousValue, float currentValue)
        {
            if (!IsServer)
            {
                _health.ApplyNetworkState(currentValue);
            }
        }

        private void HandleActiveChanged(bool previousValue, bool currentValue)
        {
            ApplyCombatState(currentValue);
        }

        private void ApplyCombatState(bool active)
        {
            _authority?.SetServerSimulationEnabled(active);

            if (_renderers != null)
            {
                foreach (Renderer targetRenderer in _renderers)
                {
                    if (targetRenderer != null)
                    {
                        targetRenderer.enabled = active;
                    }
                }
            }

            if (_colliders != null)
            {
                foreach (Collider targetCollider in _colliders)
                {
                    if (targetCollider != null)
                    {
                        targetCollider.enabled = active;
                    }
                }
            }

            if (_canvases != null)
            {
                foreach (Canvas targetCanvas in _canvases)
                {
                    if (targetCanvas != null)
                    {
                        targetCanvas.enabled = active;
                    }
                }
            }
        }

        private bool TryResolvePlayerClientId(GameObject damageSource, out ulong clientId)
        {
            clientId = default;
            if (damageSource == null || NetworkManager == null)
            {
                return false;
            }

            NetworkObject sourceNetworkObject = damageSource.GetComponentInParent<NetworkObject>();
            if (sourceNetworkObject == null || !sourceNetworkObject.IsPlayerObject ||
                !NetworkManager.ConnectedClients.ContainsKey(sourceNetworkObject.OwnerClientId))
            {
                return false;
            }

            clientId = sourceNetworkObject.OwnerClientId;
            return true;
        }

        [ClientRpc]
        private void PlayDeathEffectsClientRpc(Vector3 deathPosition)
        {
            if (_enemyController == null || _enemyController.DeathVfx == null)
            {
                return;
            }

            Vector3 spawnPosition = _enemyController.DeathVfxSpawnPoint != null
                ? _enemyController.DeathVfxSpawnPoint.position
                : deathPosition;
            GameObject effect = Instantiate(_enemyController.DeathVfx, spawnPosition, Quaternion.identity);
            Destroy(effect, 5f);
        }
    }
}
