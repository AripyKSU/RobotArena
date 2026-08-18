using UnityEngine;

namespace FPSBasic.Multiplayer.Enemies
{
    /// <summary>Non-authoritative remote visual for a server-simulated enemy projectile.</summary>
    public sealed class EnemyProjectileVisual : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _remainingLifetime;

        public void Initialize(Vector3 direction, float speed, float lifetime)
        {
            Vector3 normalizedDirection = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
            transform.forward = normalizedDirection;
            _velocity = normalizedDirection * speed;
            _remainingLifetime = lifetime;
        }

        private void Update()
        {
            transform.position += _velocity * Time.deltaTime;
            _remainingLifetime -= Time.deltaTime;
            if (_remainingLifetime <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
