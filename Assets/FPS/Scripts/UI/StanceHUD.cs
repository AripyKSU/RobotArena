using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class StanceHUD : MonoBehaviour
    {
        [Tooltip("Image component for the stance sprites")]
        public Image StanceImage;

        [Tooltip("Sprite to display when standing")]
        public Sprite StandingSprite;

        [Tooltip("Sprite to display when crouching")]
        public Sprite CrouchingSprite;

        PlayerCharacterController m_Character;

        void OnEnable()
        {
            LocalPlayerRegistry.Changed += OnLocalPlayerChanged;
            if (LocalPlayerRegistry.Current != null)
            {
                BindCharacter(LocalPlayerRegistry.Current.GetComponent<PlayerCharacterController>());
            }
        }

        void Start()
        {
            if (m_Character == null)
            {
                PlayerCharacterController character = LocalPlayerRegistry.Current != null
                    ? LocalPlayerRegistry.Current.GetComponent<PlayerCharacterController>()
                    : FindFirstObjectByType<PlayerCharacterController>();
                BindCharacter(character);
            }
        }

        void OnDisable()
        {
            LocalPlayerRegistry.Changed -= OnLocalPlayerChanged;
            BindCharacter(null);
        }

        void OnLocalPlayerChanged(GameObject localPlayer)
        {
            BindCharacter(localPlayer != null
                ? localPlayer.GetComponent<PlayerCharacterController>()
                : null);
        }

        void BindCharacter(PlayerCharacterController character)
        {
            if (m_Character == character)
            {
                return;
            }

            if (m_Character != null)
            {
                m_Character.OnStanceChanged -= OnStanceChanged;
            }

            m_Character = character;
            if (m_Character == null)
            {
                return;
            }

            m_Character.OnStanceChanged += OnStanceChanged;
            OnStanceChanged(m_Character.IsCrouching);
        }

        void OnStanceChanged(bool crouched)
        {
            StanceImage.sprite = crouched ? CrouchingSprite : StandingSprite;
        }
    }
}
