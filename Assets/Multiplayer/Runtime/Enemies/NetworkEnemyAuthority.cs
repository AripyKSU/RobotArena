using Unity.FPS.AI;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FPSBasic.Multiplayer.Enemies
{
    /// <summary>Keeps AI, navigation and weapon simulation server-authoritative.</summary>
    public sealed class NetworkEnemyAuthority : NetworkBehaviour
    {
        private EnemyController _enemyController;
        private EnemyMobile _enemyMobile;
        private NavMeshAgent _navMeshAgent;
        private WeaponController[] _weapons;

        private void Awake()
        {
            _enemyController = GetComponent<EnemyController>();
            _enemyMobile = GetComponent<EnemyMobile>();
            _navMeshAgent = GetComponent<NavMeshAgent>();
            _weapons = GetComponentsInChildren<WeaponController>(true);
        }

        public override void OnNetworkSpawn()
        {
            SetServerSimulationEnabled(IsServer);
        }

        public void SetServerSimulationEnabled(bool active)
        {
            bool enableSimulation = IsServer && active;

            if (_enemyController != null)
            {
                _enemyController.enabled = enableSimulation;
            }

            if (_enemyMobile != null)
            {
                _enemyMobile.enabled = enableSimulation;
            }

            if (_navMeshAgent != null)
            {
                if (!enableSimulation && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
                {
                    _navMeshAgent.ResetPath();
                }

                _navMeshAgent.enabled = enableSimulation;
            }

            if (_weapons != null)
            {
                foreach (WeaponController weapon in _weapons)
                {
                    if (weapon != null)
                    {
                        weapon.enabled = enableSimulation;
                    }
                }
            }
        }
    }
}
