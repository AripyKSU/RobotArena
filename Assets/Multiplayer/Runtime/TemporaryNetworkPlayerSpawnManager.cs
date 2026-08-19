using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// Temporary validation spawner. It creates one player object per connected lobby member when
    /// MainScene finishes loading; production spawn selection will replace this class later.
    /// </summary>
    public sealed class TemporaryNetworkPlayerSpawnManager : MonoBehaviour
    {
        private const string MainSceneName = "MainScene";
        private const string TeamASpawnPointName = "Team1 SpawnPoint";
        private const string TeamBSpawnPointName = "Team2 SpawnPoint";

        [SerializeField] private NetworkObject teamAPlayerPrefab;
        [SerializeField] private NetworkObject teamBPlayerPrefab;

        private readonly HashSet<ulong> _spawnedClientIds = new();
        private readonly HashSet<ulong> _expectedClientIds = new();
        private NetworkSceneManager _subscribedSceneManager;
        private Coroutine _spawnRoutine;

        private void Start()
        {
            Subscribe();
            SceneManager.sceneLoaded += OnUnitySceneLoaded;
        }

        private void Update()
        {
            // NetworkSceneManager is created only after NGO starts listening. Start() normally
            // runs earlier in StartScene, so retry until the synchronized scene event is available.
            // NGO creates a new NetworkSceneManager after shutdown/restart, so also rebind when
            // the instance changes between rooms in the same play session.
            Subscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
        }

        private void Subscribe()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            NetworkSceneManager currentSceneManager = networkManager != null
                ? networkManager.SceneManager
                : null;
            if (ReferenceEquals(_subscribedSceneManager, currentSceneManager))
            {
                return;
            }

            Unsubscribe();
            if (currentSceneManager == null)
            {
                return;
            }

            currentSceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            _subscribedSceneManager = currentSceneManager;
        }

        private void Unsubscribe()
        {
            if (_subscribedSceneManager == null)
            {
                return;
            }

            _subscribedSceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            _subscribedSceneManager = null;
        }

        private void OnLoadEventCompleted(
            string sceneName,
            LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (sceneName == MainSceneName && networkManager != null && networkManager.IsServer)
            {
                if (clientsTimedOut.Count > 0)
                {
                    Debug.LogError(
                        $"[Spawn] MainScene load timed out for client(s): {string.Join(", ", clientsTimedOut)}. " +
                        "Player spawning was cancelled to avoid starting a partial match.");
                    return;
                }

                foreach (ulong connectedClientId in networkManager.ConnectedClientsIds)
                {
                    if (!clientsCompleted.Contains(connectedClientId))
                    {
                        Debug.LogError(
                            $"[Spawn] Client {connectedClientId} did not complete MainScene loading. " +
                            "Player spawning was cancelled.");
                        return;
                    }
                }

                _expectedClientIds.Clear();
                foreach (ulong clientId in clientsCompleted)
                {
                    _expectedClientIds.Add(clientId);
                }

                RequestSpawn();
            }
            else if (sceneName != MainSceneName)
            {
                _spawnedClientIds.Clear();
                _expectedClientIds.Clear();
                ServerGameplayReadySignal.Reset();
            }
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            if (scene.name == MainSceneName)
            {
                // MainScene owns a loading camera until LocalPlayerRegistry receives the owned PlayerObject.
                Debug.Log("[Spawn] MainScene activated locally; loading presentation is waiting for PlayerObject.");
            }
            else if (scene.name != MainSceneName)
            {
                _spawnedClientIds.Clear();
            }
        }

        private void RequestSpawn()
        {
            if (_spawnRoutine == null)
            {
                _spawnRoutine = StartCoroutine(SpawnPlayersWhenRoomStateIsReady());
            }
        }

        private IEnumerator SpawnPlayersWhenRoomStateIsReady()
        {
            // Let the LoadEventCompleted message finish on every peer before adding spawn and
            // transform traffic. This is especially important after a synchronous, asset-heavy load.
            yield return null;
            yield return new WaitForEndOfFrame();

            const float timeoutSeconds = 30f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            RoomNetworkState roomState = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                roomState = RoomNetworkState.LocalInstance;
                NetworkManager networkManager = NetworkManager.Singleton;
                if (roomState != null && networkManager != null && networkManager.IsServer &&
                    AreExpectedPlayersReady(roomState, networkManager))
                {
                    break;
                }

                yield return null;
            }

            _spawnRoutine = null;
            NetworkManager currentNetworkManager = NetworkManager.Singleton;
            if (roomState == null || RoomNetworkState.LocalInstance != roomState ||
                currentNetworkManager == null ||
                !AreExpectedPlayersReady(roomState, currentNetworkManager))
            {
                Debug.LogError(
                    "[Spawn] Timed out waiting for every loaded client to remain connected and " +
                    "registered in RoomNetworkState. Player spawning was cancelled.");
                yield break;
            }

            Transform teamASpawn = FindSpawnPoint(TeamASpawnPointName);
            Transform teamBSpawn = FindSpawnPoint(TeamBSpawnPointName);
            if (teamASpawn == null || teamBSpawn == null || teamAPlayerPrefab == null || teamBPlayerPrefab == null)
            {
                Debug.LogError("[Spawn] Player prefabs or team spawn points are not assigned.");
                yield break;
            }

            var playersToSpawn = new List<RoomPlayerState>();
            foreach (RoomPlayerState player in roomState.Players)
            {
                if (!_expectedClientIds.Contains(player.ClientId))
                {
                    continue;
                }

                playersToSpawn.Add(player);
            }

            // Give remote clients their owned camera first. A host-side initialization failure
            // must never prevent a remote player's SpawnAsPlayerObject call.
            playersToSpawn.Sort((left, right) =>
            {
                bool leftIsServer = left.ClientId == NetworkManager.ServerClientId;
                bool rightIsServer = right.ClientId == NetworkManager.ServerClientId;
                if (leftIsServer == rightIsServer)
                {
                    return left.ClientId.CompareTo(right.ClientId);
                }

                return leftIsServer ? 1 : -1;
            });

            foreach (RoomPlayerState player in playersToSpawn)
            {

                if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(
                        player.ClientId,
                        out NetworkClient connectedClient))
                {
                    continue;
                }

                // NGO permits only one PlayerObject for a client. Scene callbacks can arrive
                // more than once during a synchronized load, so use NGO's authoritative state
                // as well as this manager's local guard before creating a replacement.
                if (_spawnedClientIds.Contains(player.ClientId) || connectedClient.PlayerObject != null)
                {
                    _spawnedClientIds.Add(player.ClientId);
                    Debug.LogWarning(
                        $"[Spawn] Skipped Client={player.ClientId}; existing PlayerObject=" +
                        $"{(connectedClient.PlayerObject != null ? connectedClient.PlayerObject.name : "tracked locally")}.");
                    continue;
                }

                bool isTeamA = player.Team == RoomTeam.TeamA;
                Transform spawnPoint = isTeamA ? teamASpawn : teamBSpawn;
                NetworkObject prefab = isTeamA ? teamAPlayerPrefab : teamBPlayerPrefab;
                NetworkObject playerObject = null;
                try
                {
                    playerObject = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
                    playerObject.SpawnAsPlayerObject(player.ClientId, true);
                    _spawnedClientIds.Add(player.ClientId);

                    Debug.Log(
                        $"[Spawn] Client={player.ClientId} Team={player.Team} " +
                        $"Prefab={prefab.name} NetworkObjectId={playerObject.NetworkObjectId}");
                }
                catch (System.Exception exception)
                {
                    Debug.LogError(
                        $"[Spawn] Failed to create PlayerObject for Client={player.ClientId} " +
                        $"Team={player.Team}.\n{exception}");

                    if (playerObject != null && !playerObject.IsSpawned)
                    {
                        Destroy(playerObject.gameObject);
                    }
                }
            }

            bool allPlayerObjectsSpawned = true;
            foreach (ulong expectedClientId in _expectedClientIds)
            {
                if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(
                        expectedClientId,
                        out NetworkClient connectedClient) ||
                    connectedClient.PlayerObject == null)
                {
                    allPlayerObjectsSpawned = false;
                    Debug.LogError($"[Spawn] Client {expectedClientId} has no PlayerObject after the spawn pass.");
                }
            }

            if (allPlayerObjectsSpawned)
            {
                ServerGameplayReadySignal.Raise();
                Debug.Log("[Spawn] Every expected PlayerObject exists; server gameplay systems may start.");
            }
        }

        private bool AreExpectedPlayersReady(
            RoomNetworkState roomState,
            NetworkManager networkManager)
        {
            MatchModeRules rules = MatchModeRules.For(roomState.Mode.Value);
            int requiredPlayerCount = rules.TeamACapacity + rules.TeamBCapacity;
            if (_expectedClientIds.Count != requiredPlayerCount ||
                roomState.Players.Count < _expectedClientIds.Count)
            {
                return false;
            }

            foreach (ulong expectedClientId in _expectedClientIds)
            {
                if (!networkManager.ConnectedClients.ContainsKey(expectedClientId))
                {
                    return false;
                }

                if (!roomState.IsClientMainSceneReady(expectedClientId))
                {
                    return false;
                }

                bool foundInRoom = false;
                foreach (RoomPlayerState player in roomState.Players)
                {
                    if (player.ClientId == expectedClientId)
                    {
                        foundInRoom = true;
                        break;
                    }
                }

                if (!foundInRoom)
                {
                    return false;
                }
            }

            return true;
        }

        private static Transform FindSpawnPoint(string objectName)
        {
            GameObject spawnPoint = GameObject.Find(objectName);
            return spawnPoint != null ? spawnPoint.transform : null;
        }

    }
}
