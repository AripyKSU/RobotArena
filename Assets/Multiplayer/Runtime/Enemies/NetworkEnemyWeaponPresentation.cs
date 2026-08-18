using System;
using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer.Enemies
{
    /// <summary>Replicates enemy projectile presentation while the server owns hit simulation.</summary>
    public sealed class NetworkEnemyWeaponPresentation : NetworkBehaviour
    {
        [SerializeField] private EnemyProjectileVisual visualProjectilePrefab;

        private WeaponController[] _weapons;
        private readonly Dictionary<WeaponController, Action<Vector3, Vector3>> _shotHandlers = new();
        private readonly List<EnemyProjectileVisual> _activeVisuals = new();

        private void Awake()
        {
            _weapons = GetComponentsInChildren<WeaponController>(true);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer || _weapons == null)
            {
                return;
            }

            foreach (WeaponController weapon in _weapons)
            {
                if (weapon != null)
                {
                    WeaponController capturedWeapon = weapon;
                    Action<Vector3, Vector3> handler =
                        (origin, direction) => HandleServerProjectileFired(capturedWeapon, origin, direction);
                    _shotHandlers.Add(weapon, handler);
                    weapon.OnProjectileFired += handler;
                }
            }

            ProjectileStandard.AnyImpact += HandleServerProjectileImpact;
        }

        public override void OnNetworkDespawn()
        {
            if (_weapons == null)
            {
                return;
            }

            foreach (WeaponController weapon in _weapons)
            {
                if (weapon != null && _shotHandlers.TryGetValue(weapon, out Action<Vector3, Vector3> handler))
                {
                    weapon.OnProjectileFired -= handler;
                }
            }

            _shotHandlers.Clear();
            if (IsServer)
            {
                ProjectileStandard.AnyImpact -= HandleServerProjectileImpact;
            }
        }

        private void HandleServerProjectileFired(WeaponController weapon, Vector3 origin, Vector3 direction)
        {
            ProjectileStandard projectile = weapon != null && weapon.ProjectilePrefab != null
                ? weapon.ProjectilePrefab.GetComponent<ProjectileStandard>()
                : null;

            if (projectile == null)
            {
                Debug.LogError($"[EnemyProjectile] {name} could not resolve ProjectileStandard settings.", this);
                return;
            }

            PlayProjectileVisualClientRpc(
                origin,
                direction.normalized,
                projectile.Speed,
                projectile.MaxLifeTime);
        }

        private void HandleServerProjectileImpact(
            ProjectileStandard projectile,
            Vector3 point,
            Vector3 normal)
        {
            if (!IsServer || projectile == null || projectile.Owner == null)
            {
                return;
            }

            NetworkEnemyWeaponPresentation ownerPresentation =
                projectile.Owner.GetComponentInParent<NetworkEnemyWeaponPresentation>();
            if (ownerPresentation != this)
            {
                return;
            }

            PlayProjectileImpactClientRpc(
                point,
                normal,
                projectile.ImpactVfxSpawnOffset,
                projectile.ImpactVfxLifetime);
        }

        [ClientRpc]
        private void PlayProjectileVisualClientRpc(
            Vector3 origin,
            Vector3 direction,
            float speed,
            float lifetime)
        {
            // The host already sees the authoritative server projectile.
            if (IsServer || visualProjectilePrefab == null)
            {
                return;
            }

            EnemyProjectileVisual visual = Instantiate(
                visualProjectilePrefab,
                origin,
                Quaternion.LookRotation(direction));
            visual.Initialize(direction, speed, lifetime);
            _activeVisuals.Add(visual);
        }

        [ClientRpc]
        private void PlayProjectileImpactClientRpc(
            Vector3 point,
            Vector3 normal,
            float impactOffset,
            float impactLifetime)
        {
            if (IsServer)
            {
                return;
            }

            RemoveClosestVisual(point);

            ProjectileStandard projectileSettings = GetProjectileSettings();
            if (projectileSettings == null || projectileSettings.ImpactVfx == null)
            {
                return;
            }

            GameObject impact = Instantiate(
                projectileSettings.ImpactVfx,
                point + normal * impactOffset,
                Quaternion.LookRotation(normal));
            if (impactLifetime > 0f)
            {
                Destroy(impact, impactLifetime);
            }
        }

        private ProjectileStandard GetProjectileSettings()
        {
            if (_weapons == null)
            {
                return null;
            }

            foreach (WeaponController weapon in _weapons)
            {
                if (weapon != null && weapon.ProjectilePrefab != null &&
                    weapon.ProjectilePrefab.TryGetComponent(out ProjectileStandard projectile))
                {
                    return projectile;
                }
            }

            return null;
        }

        private void RemoveClosestVisual(Vector3 impactPoint)
        {
            EnemyProjectileVisual closest = null;
            float closestSqrDistance = float.PositiveInfinity;

            for (int i = _activeVisuals.Count - 1; i >= 0; i--)
            {
                EnemyProjectileVisual visual = _activeVisuals[i];
                if (visual == null)
                {
                    _activeVisuals.RemoveAt(i);
                    continue;
                }

                float sqrDistance = (visual.transform.position - impactPoint).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closest = visual;
                    closestSqrDistance = sqrDistance;
                }
            }

            if (closest != null)
            {
                _activeVisuals.Remove(closest);
                Destroy(closest.gameObject);
            }
        }
    }
}
