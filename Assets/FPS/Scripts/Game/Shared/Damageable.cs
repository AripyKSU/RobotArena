using UnityEngine;

namespace Unity.FPS.Game
{
    public class Damageable : MonoBehaviour
    {
        // Multiplayer adapters can consume a client-side damage candidate without making the
        // original FPS assembly depend on a networking assembly.
        public static System.Func<Damageable, float, bool, GameObject, bool> TryHandleNetworkDamage;

        [Tooltip("Multiplier to apply to the received damage")]
        public float DamageMultiplier = 1f;

        [Range(0, 1)] [Tooltip("Multiplier to apply to self damage")]
        public float SensibilityToSelfdamage = 0.5f;

        public Health Health { get; private set; }

        void Awake()
        {
            // find the health component either at the same level, or higher in the hierarchy
            Health = GetComponent<Health>();
            if (!Health)
            {
                Health = GetComponentInParent<Health>();
            }
        }

        public void InflictDamage(float damage, bool isExplosionDamage, GameObject damageSource)
        {
            if (Health)
            {
                if (TryHandleNetworkDamage != null &&
                    TryHandleNetworkDamage.Invoke(this, damage, isExplosionDamage, damageSource))
                {
                    return;
                }

                var totalDamage = damage;

                // skip the crit multiplier if it's from an explosion
                if (!isExplosionDamage)
                {
                    totalDamage *= DamageMultiplier;
                }

                // potentially reduce damages if inflicted by self
                if (Health.gameObject == damageSource)
                {
                    totalDamage *= SensibilityToSelfdamage;
                }

                // apply the damages
                Health.TakeDamage(totalDamage, damageSource);
            }
        }
    }
}
