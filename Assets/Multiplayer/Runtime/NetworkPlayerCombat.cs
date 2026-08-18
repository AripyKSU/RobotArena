using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using FPSBasic.Multiplayer.Enemies;

namespace FPSBasic.Multiplayer
{
    /// <summary>Routes a client-side player hit candidate to server-side damage calculation.</summary>
    public sealed class NetworkPlayerCombat : NetworkBehaviour
    {
        private readonly HashSet<WeaponController> _observedWeapons = new();
        private PlayerWeaponsManager _weaponsManager;

        public override void OnNetworkSpawn()
        {
            Damageable.TryHandleNetworkDamage = TryRouteClientDamage;

            if (!IsOwner)
            {
                return;
            }

            _weaponsManager = GetComponent<PlayerWeaponsManager>();
            if (_weaponsManager == null)
            {
                return;
            }

            _weaponsManager.OnAddedWeapon += OnWeaponAdded;
            _weaponsManager.OnRemovedWeapon += OnWeaponRemoved;
            for (int i = 0; i < _weaponsManager.StartingWeapons.Count; i++)
            {
                ObserveWeapon(_weaponsManager.GetWeaponAtSlotIndex(i));
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_weaponsManager != null)
            {
                _weaponsManager.OnAddedWeapon -= OnWeaponAdded;
                _weaponsManager.OnRemovedWeapon -= OnWeaponRemoved;
            }

            foreach (WeaponController weapon in _observedWeapons)
            {
                if (weapon != null) weapon.OnProjectileFired -= OnLocalProjectileFired;
            }

            _observedWeapons.Clear();
        }

        public void ReportHit(NetworkObject target, float damage, bool isExplosionDamage)
        {
            if (IsOwner && target != null && target.IsSpawned)
                ReportHitServerRpc(target, damage, isExplosionDamage);
        }

        [ServerRpc]
        private void ReportHitServerRpc(NetworkObjectReference targetReference, float damage, bool isExplosionDamage)
        {
            if (!float.IsFinite(damage) || damage <= 0f || damage > 1000f) return;
            if (!targetReference.TryGet(out NetworkObject targetObject)) return;

            NetworkEnemyLifecycle enemy = targetObject.GetComponent<NetworkEnemyLifecycle>();
            if (enemy != null && !enemy.IsCombatActive.Value) return;

            Damageable damageable = targetObject.GetComponent<Damageable>();
            if (damageable == null) damageable = targetObject.GetComponentInChildren<Damageable>();
            if (damageable != null) damageable.InflictDamage(damage, isExplosionDamage, gameObject);
        }

        private void OnWeaponAdded(WeaponController weapon, int slotIndex) => ObserveWeapon(weapon);

        private void OnWeaponRemoved(WeaponController weapon, int slotIndex)
        {
            if (weapon != null && _observedWeapons.Remove(weapon))
            {
                weapon.OnProjectileFired -= OnLocalProjectileFired;
            }
        }

        private void ObserveWeapon(WeaponController weapon)
        {
            if (weapon != null && _observedWeapons.Add(weapon))
            {
                weapon.OnProjectileFired += OnLocalProjectileFired;
            }
        }

        private void OnLocalProjectileFired(Vector3 origin, Vector3 direction)
        {
            if (IsOwner && IsSpawned)
            {
                ReportShotServerRpc(origin, direction.normalized);
            }
        }

        [ServerRpc]
        private void ReportShotServerRpc(Vector3 origin, Vector3 direction)
        {
            ShowShotClientRpc(origin, direction);
        }

        [ClientRpc]
        private void ShowShotClientRpc(Vector3 origin, Vector3 direction)
        {
            if (!IsOwner)
            {
                StartCoroutine(ShowRemoteTracer(origin, direction));
            }
        }

        private static IEnumerator ShowRemoteTracer(Vector3 origin, Vector3 direction)
        {
            GameObject tracerObject = new("NetworkBulletTracer");
            LineRenderer tracer = tracerObject.AddComponent<LineRenderer>();
            Material tracerMaterial = new(Shader.Find("Sprites/Default"));
            tracer.material = tracerMaterial;
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.startWidth = 0.035f;
            tracer.endWidth = 0.01f;
            tracer.startColor = new Color(1f, 0.85f, 0.2f, 1f);
            tracer.endColor = new Color(1f, 0.35f, 0.05f, 0.15f);
            tracer.numCapVertices = 2;
            tracer.SetPosition(0, origin);
            tracer.SetPosition(1, origin + direction.normalized * 4f);

            yield return new WaitForSeconds(0.08f);

            Destroy(tracerMaterial);
            Destroy(tracerObject);
        }

        private static bool TryRouteClientDamage(
            Damageable damageable,
            float damage,
            bool isExplosionDamage,
            GameObject damageSource)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening || networkManager.IsServer)
            {
                return false;
            }

            NetworkPlayerCombat attacker = damageSource != null
                ? damageSource.GetComponent<NetworkPlayerCombat>()
                : null;
            NetworkPlayerHealth playerTarget = damageable.Health != null
                ? damageable.Health.GetComponent<NetworkPlayerHealth>()
                : null;
            NetworkEnemyLifecycle enemyTarget = damageable.Health != null
                ? damageable.Health.GetComponent<NetworkEnemyLifecycle>()
                : null;
            NetworkObject targetObject = playerTarget != null
                ? playerTarget.NetworkObject
                : enemyTarget != null
                    ? enemyTarget.NetworkObject
                    : null;

            if (attacker == null || !attacker.IsOwner || targetObject == null || !targetObject.IsSpawned ||
                (enemyTarget != null && !enemyTarget.IsCombatActive.Value))
            {
                return false;
            }

            attacker.ReportHit(targetObject, damage, isExplosionDamage);
            return true;
        }
    }
}
