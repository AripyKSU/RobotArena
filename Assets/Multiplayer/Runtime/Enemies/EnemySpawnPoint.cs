using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FPSBasic.Multiplayer.Enemies
{
    /// <summary>Owns one server-side pool of network enemies and samples their NavMesh positions.</summary>
    public sealed class EnemySpawnPoint : MonoBehaviour
    {
        [Header("Pool")]
        [SerializeField] private NetworkEnemyLifecycle enemyPrefab;
        [SerializeField, Min(1)] private int poolSize = 5;
        [SerializeField, Min(0f)] private float respawnDelay = 10f;

        [Header("Random Area")]
        [SerializeField] private Vector3 areaCenter = Vector3.zero;
        [SerializeField] private Vector3 areaSize = new(12f, 4f, 12f);
        [SerializeField, Min(0f)] private float minimumEnemySpacing = 2f;
        [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 3f;
        [SerializeField, Min(1)] private int placementAttempts = 30;

        [Header("Scene Preview")]
        [SerializeField] private bool alwaysShowArea = true;

        private readonly List<NetworkEnemyLifecycle> _pool = new();
        private readonly HashSet<NetworkEnemyLifecycle> _waitingForRespawn = new();
        private readonly List<Vector3> _previewPositions = new();
        private bool _initialized;

        public IReadOnlyList<Vector3> PreviewPositions => _previewPositions;
        public int PoolSize => poolSize;
        public float RespawnDelay => respawnDelay;

        public void ServerInitializePool()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (_initialized || networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            if (enemyPrefab == null)
            {
                Debug.LogError($"[EnemySpawn] {name} has no enemy prefab assigned.", this);
                return;
            }

            _initialized = true;
            for (int i = 0; i < poolSize; i++)
            {
                if (!TryGetSpawnPose(out Vector3 position, out Quaternion rotation))
                {
                    position = transform.TransformPoint(areaCenter);
                    rotation = transform.rotation;
                    Debug.LogWarning(
                        $"[EnemySpawn] {name} could not find a separated NavMesh point for slot {i}. " +
                        "Using the area center fallback.",
                        this);
                }

                NetworkEnemyLifecycle enemy = Instantiate(enemyPrefab, position, rotation);
                enemy.name = $"{enemyPrefab.name}_{i + 1}";
                enemy.ServerBind(this);
                _pool.Add(enemy);
                enemy.NetworkObject.Spawn(true);
                enemy.ServerActivate(position, rotation);
            }
        }

        public void ServerScheduleRespawn(NetworkEnemyLifecycle enemy)
        {
            if (enemy == null || !_pool.Contains(enemy) || !_waitingForRespawn.Add(enemy))
            {
                return;
            }

            StartCoroutine(RespawnAfterDelay(enemy));
        }

        public bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            float halfX = Mathf.Abs(areaSize.x) * 0.5f;
            float halfY = Mathf.Abs(areaSize.y) * 0.5f;
            float halfZ = Mathf.Abs(areaSize.z) * 0.5f;

            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                Vector3 localCandidate = areaCenter + new Vector3(
                    Random.Range(-halfX, halfX),
                    Random.Range(-halfY, halfY),
                    Random.Range(-halfZ, halfZ));
                Vector3 worldCandidate = transform.TransformPoint(localCandidate);

                if (!NavMesh.SamplePosition(worldCandidate, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
                {
                    continue;
                }

                Vector3 sampledLocal = transform.InverseTransformPoint(hit.position) - areaCenter;
                if (Mathf.Abs(sampledLocal.x) > halfX || Mathf.Abs(sampledLocal.z) > halfZ)
                {
                    continue;
                }

                if (!HasMinimumSpacing(hit.position))
                {
                    continue;
                }

                position = hit.position;
                rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                return true;
            }

            position = default;
            rotation = transform.rotation;
            return false;
        }

        [ContextMenu("Preview Spawn Positions")]
        public void GeneratePreviewPositions()
        {
            _previewPositions.Clear();
            for (int i = 0; i < poolSize; i++)
            {
                if (TryGetPreviewPosition(_previewPositions, out Vector3 position))
                {
                    _previewPositions.Add(position);
                }
            }
        }

        private IEnumerator RespawnAfterDelay(NetworkEnemyLifecycle enemy)
        {
            yield return new WaitForSecondsRealtime(respawnDelay);

            if (enemy != null && enemy.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                if (!TryGetSpawnPose(out Vector3 position, out Quaternion rotation))
                {
                    position = transform.TransformPoint(areaCenter);
                    rotation = transform.rotation;
                }

                enemy.ServerActivate(position, rotation);
            }

            _waitingForRespawn.Remove(enemy);
        }

        private bool HasMinimumSpacing(Vector3 candidate)
        {
            float requiredSqrDistance = minimumEnemySpacing * minimumEnemySpacing;
            foreach (NetworkEnemyLifecycle enemy in _pool)
            {
                if (enemy != null && enemy.IsCombatActive.Value &&
                    (enemy.transform.position - candidate).sqrMagnitude < requiredSqrDistance)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetPreviewPosition(List<Vector3> existing, out Vector3 position)
        {
            float halfX = Mathf.Abs(areaSize.x) * 0.5f;
            float halfY = Mathf.Abs(areaSize.y) * 0.5f;
            float halfZ = Mathf.Abs(areaSize.z) * 0.5f;
            float requiredSqrDistance = minimumEnemySpacing * minimumEnemySpacing;

            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                Vector3 localCandidate = areaCenter + new Vector3(
                    Random.Range(-halfX, halfX),
                    Random.Range(-halfY, halfY),
                    Random.Range(-halfZ, halfZ));
                Vector3 worldCandidate = transform.TransformPoint(localCandidate);
                if (!NavMesh.SamplePosition(worldCandidate, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
                {
                    continue;
                }

                bool overlaps = false;
                foreach (Vector3 other in existing)
                {
                    if ((other - hit.position).sqrMagnitude < requiredSqrDistance)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    position = hit.position;
                    return true;
                }
            }

            position = default;
            return false;
        }

        private void OnDrawGizmos()
        {
            if (alwaysShowArea)
            {
                DrawArea(false);
            }
        }

        private void OnDrawGizmosSelected()
        {
            DrawArea(true);
        }

        private void DrawArea(bool selected)
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = selected
                ? new Color(0.1f, 0.9f, 1f, 0.18f)
                : new Color(0.1f, 0.9f, 1f, 0.7f);

            if (selected)
            {
                Gizmos.DrawCube(areaCenter, areaSize);
            }

            Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.95f);
            Gizmos.DrawWireCube(areaCenter, areaSize);
            Gizmos.matrix = previousMatrix;

            Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.9f);
            foreach (Vector3 previewPosition in _previewPositions)
            {
                Gizmos.DrawSphere(previewPosition, 0.2f);
                Gizmos.DrawWireSphere(previewPosition, minimumEnemySpacing * 0.5f);
            }

            Gizmos.color = previousColor;
        }
    }
}
