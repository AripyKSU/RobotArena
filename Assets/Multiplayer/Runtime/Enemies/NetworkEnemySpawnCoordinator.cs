using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer.Enemies
{
    /// <summary>Bootstraps every scene EnemySpawnPoint when both network players are ready.</summary>
    public sealed class NetworkEnemySpawnCoordinator : MonoBehaviour
    {
        private static NetworkEnemySpawnCoordinator _instance;
        private bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            GameObject coordinatorObject = new("NetworkEnemySpawnCoordinator");
            DontDestroyOnLoad(coordinatorObject);
            _instance = coordinatorObject.AddComponent<NetworkEnemySpawnCoordinator>();
        }

        private void OnEnable()
        {
            ServerGameplayReadySignal.Raised += HandleGameplayReady;
            ServerGameplayReadySignal.ResetPerformed += HandleGameplayReset;

            if (ServerGameplayReadySignal.IsRaised)
            {
                HandleGameplayReady();
            }
        }

        private void OnDisable()
        {
            ServerGameplayReadySignal.Raised -= HandleGameplayReady;
            ServerGameplayReadySignal.ResetPerformed -= HandleGameplayReset;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void HandleGameplayReady()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (_initialized || networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            EnemySpawnPoint[] spawnPoints = FindObjectsByType<EnemySpawnPoint>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (spawnPoints.Length == 0)
            {
                Debug.LogWarning("[EnemySpawn] Gameplay is ready, but the scene contains no EnemySpawnPoint.");
                return;
            }

            _initialized = true;
            foreach (EnemySpawnPoint spawnPoint in spawnPoints)
            {
                spawnPoint.ServerInitializePool();
            }
        }

        private void HandleGameplayReset()
        {
            _initialized = false;
        }
    }
}
