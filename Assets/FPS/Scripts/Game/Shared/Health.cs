using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Game
{
    public class Health : MonoBehaviour
    {
        [Tooltip("Maximum amount of health")] public float MaxHealth = 10f;

        [Tooltip("Health ratio at which the critical health vignette starts appearing")]
        public float CriticalHealthRatio = 0.3f;

        public UnityAction<float, GameObject> OnDamaged;
        public UnityAction<float> OnHealed;
        public UnityAction OnDie;

        public float CurrentHealth { get; set; }
        public bool Invincible { get; set; }
        public bool CanPickup() => CurrentHealth < MaxHealth;

        public float GetRatio() => CurrentHealth / MaxHealth;
        public bool IsCritical() => GetRatio() <= CriticalHealthRatio;

        bool m_IsDead;

        void Awake()
        {
            CurrentHealth = MaxHealth;
        }

        public void ApplyNetworkState(float health, GameObject damageSource = null)
        {
            float healthBefore = CurrentHealth;
            CurrentHealth = Mathf.Clamp(health, 0f, MaxHealth);

            float delta = CurrentHealth - healthBefore;
            if (delta < 0f)
            {
                OnDamaged?.Invoke(-delta, damageSource);
            }
            else if (delta > 0f)
            {
                OnHealed?.Invoke(delta);
            }

            if (CurrentHealth > 0f)
            {
                m_IsDead = false;
            }
            else
            {
                HandleDeath();
            }
        }

        public void Revive()
        {
            float healthBefore = CurrentHealth;
            m_IsDead = false;
            CurrentHealth = MaxHealth;

            float healedAmount = CurrentHealth - healthBefore;
            if (healedAmount > 0f)
            {
                OnHealed?.Invoke(healedAmount);
            }
        }

        public void Heal(float healAmount)
        {
            float healthBefore = CurrentHealth;
            CurrentHealth += healAmount;
            CurrentHealth = Mathf.Clamp(CurrentHealth, 0f, MaxHealth);

            // call OnHeal action
            float trueHealAmount = CurrentHealth - healthBefore;
            if (trueHealAmount > 0f)
            {
                OnHealed?.Invoke(trueHealAmount);
            }
        }

        public void TakeDamage(float damage, GameObject damageSource)
        {
            if (Invincible)
                return;

            float healthBefore = CurrentHealth;
            CurrentHealth -= damage;
            CurrentHealth = Mathf.Clamp(CurrentHealth, 0f, MaxHealth);

            // call OnDamage action
            float trueDamageAmount = healthBefore - CurrentHealth;
            if (trueDamageAmount > 0f)
            {
                OnDamaged?.Invoke(trueDamageAmount, damageSource);
            }

            HandleDeath();
        }

        public void Kill()
        {
            CurrentHealth = 0f;

            // call OnDamage action
            OnDamaged?.Invoke(MaxHealth, null);

            HandleDeath();
        }

        void HandleDeath()
        {
            if (m_IsDead)
                return;

            // call OnDie action
            if (CurrentHealth <= 0f)
            {
                m_IsDead = true;
                OnDie?.Invoke();
            }
        }
    }
}
