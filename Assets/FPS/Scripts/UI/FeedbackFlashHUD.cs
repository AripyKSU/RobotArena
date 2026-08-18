using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class FeedbackFlashHUD : MonoBehaviour
    {
        [Header("References")] [Tooltip("Image component of the flash")]
        public Image FlashImage;

        [Tooltip("CanvasGroup to fade the damage flash, used when recieving damage end healing")]
        public CanvasGroup FlashCanvasGroup;

        [Tooltip("CanvasGroup to fade the critical health vignette")]
        public CanvasGroup VignetteCanvasGroup;

        [Header("Damage")] [Tooltip("Color of the damage flash")]
        public Color DamageFlashColor;

        [Tooltip("Duration of the damage flash")]
        public float DamageFlashDuration;

        [Tooltip("Max alpha of the damage flash")]
        public float DamageFlashMaxAlpha = 1f;

        [Header("Critical health")] [Tooltip("Max alpha of the critical vignette")]
        public float CriticaHealthVignetteMaxAlpha = .8f;

        [Tooltip("Frequency at which the vignette will pulse when at critical health")]
        public float PulsatingVignetteFrequency = 4f;

        [Header("Heal")] [Tooltip("Color of the heal flash")]
        public Color HealFlashColor;

        [Tooltip("Duration of the heal flash")]
        public float HealFlashDuration;

        [Tooltip("Max alpha of the heal flash")]
        public float HealFlashMaxAlpha = 1f;

        bool m_FlashActive;
        float m_LastTimeFlashStarted = Mathf.NegativeInfinity;
        Health m_PlayerHealth;

        void OnEnable()
        {
            LocalPlayerRegistry.Changed += OnLocalPlayerChanged;
            if (LocalPlayerRegistry.Current != null)
            {
                OnLocalPlayerChanged(LocalPlayerRegistry.Current);
            }
        }

        void Start()
        {
            Health preferredHealth = LocalPlayerRegistry.Current != null
                ? LocalPlayerRegistry.Current.GetComponent<Health>()
                : FindFirstObjectByType<PlayerCharacterController>()?.GetComponent<Health>();
            BindPlayerHealth(preferredHealth);

        }

        void OnDisable()
        {
            LocalPlayerRegistry.Changed -= OnLocalPlayerChanged;
            BindPlayerHealth(null);
        }

        void OnLocalPlayerChanged(GameObject localPlayer)
        {
            BindPlayerHealth(localPlayer != null ? localPlayer.GetComponent<Health>() : null);
        }

        void BindPlayerHealth(Health playerHealth)
        {
            if (m_PlayerHealth == playerHealth)
            {
                return;
            }

            if (m_PlayerHealth != null)
            {
                m_PlayerHealth.OnDamaged -= OnTakeDamage;
                m_PlayerHealth.OnHealed -= OnHealed;
            }

            m_PlayerHealth = playerHealth;
            if (m_PlayerHealth != null)
            {
                m_PlayerHealth.OnDamaged += OnTakeDamage;
                m_PlayerHealth.OnHealed += OnHealed;
            }
        }

        void Update()
        {
            if (m_PlayerHealth == null)
            {
                return;
            }

            if (m_PlayerHealth.IsCritical())
            {
                VignetteCanvasGroup.gameObject.SetActive(true);
                float vignetteAlpha =
                    (1 - (m_PlayerHealth.CurrentHealth / m_PlayerHealth.MaxHealth /
                          m_PlayerHealth.CriticalHealthRatio)) * CriticaHealthVignetteMaxAlpha;

                VignetteCanvasGroup.alpha =
                    ((Mathf.Sin(Time.time * PulsatingVignetteFrequency) / 2) + 0.5f) * vignetteAlpha;
            }
            else
            {
                VignetteCanvasGroup.gameObject.SetActive(false);
            }


            if (m_FlashActive)
            {
                float normalizedTimeSinceDamage = (Time.time - m_LastTimeFlashStarted) / DamageFlashDuration;

                if (normalizedTimeSinceDamage < 1f)
                {
                    float flashAmount = DamageFlashMaxAlpha * (1f - normalizedTimeSinceDamage);
                    FlashCanvasGroup.alpha = flashAmount;
                }
                else
                {
                    FlashCanvasGroup.gameObject.SetActive(false);
                    m_FlashActive = false;
                }
            }
        }

        void ResetFlash()
        {
            m_LastTimeFlashStarted = Time.time;
            m_FlashActive = true;
            FlashCanvasGroup.alpha = 0f;
            FlashCanvasGroup.gameObject.SetActive(true);
        }

        void OnTakeDamage(float dmg, GameObject damageSource)
        {
            ResetFlash();
            FlashImage.color = DamageFlashColor;
        }

        void OnHealed(float amount)
        {
            ResetFlash();
            FlashImage.color = HealFlashColor;
        }
    }
}
