using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class PlayerHealthBar : MonoBehaviour
    {
        [Tooltip("Image component dispplaying current health")]
        public Image HealthFillImage;

        Health m_PlayerHealth;
        static Health s_LocalPlayerHealth;

        public static void SetLocalPlayerHealth(Health health)
        {
            s_LocalPlayerHealth = health;
        }

        void Start()
        {
            TryBindPlayerHealth();
        }

        void Update()
        {
            TryBindPlayerHealth();
            if (m_PlayerHealth == null)
            {
                HealthFillImage.fillAmount = 0f;
                return;
            }

            // update health bar value
            HealthFillImage.fillAmount = m_PlayerHealth.CurrentHealth / m_PlayerHealth.MaxHealth;
        }

        void TryBindPlayerHealth()
        {
            if (s_LocalPlayerHealth != null)
            {
                m_PlayerHealth = s_LocalPlayerHealth;
                return;
            }

            if (m_PlayerHealth == null || !m_PlayerHealth.gameObject.activeInHierarchy)
            {
                PlayerCharacterController playerCharacterController =
                    GameObject.FindFirstObjectByType<PlayerCharacterController>();
                m_PlayerHealth = playerCharacterController != null
                    ? playerCharacterController.GetComponent<Health>()
                    : null;
            }
        }
    }
}
