using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class InGameMenuManager : MonoBehaviour
    {
        [Tooltip("Root GameObject of the menu used to toggle its activation")]
        public GameObject MenuRoot;

        [Tooltip("Master volume when menu is open")] [Range(0.001f, 1f)]
        public float VolumeWhenMenuOpen = 0.5f;

        [Tooltip("Slider component for look sensitivity")]
        public Slider LookSensitivitySlider;

        [Tooltip("Toggle component for shadows")]
        public Toggle ShadowsToggle;

        [Tooltip("Toggle component for invincibility")]
        public Toggle InvincibilityToggle;

        [Tooltip("GameObject for the controls")]
        public GameObject ControlImage;

        PlayerInputHandler m_PlayerInputsHandler;
        Health m_PlayerHealth;
        
        private InputAction m_SubmitAction;
        private InputAction m_CancelAction;
        private InputAction m_NavigateAction;
        private InputAction m_MenuAction;

        void OnEnable()
        {
            LocalPlayerRegistry.Changed += OnLocalPlayerChanged;
            if (LocalPlayerRegistry.Current != null)
            {
                BindPlayer(LocalPlayerRegistry.Current);
            }
        }

        void OnDisable()
        {
            LocalPlayerRegistry.Changed -= OnLocalPlayerChanged;
            BindPlayer(null);
        }

        void Start()
        {
            if (m_PlayerInputsHandler == null)
            {
                GameObject player = LocalPlayerRegistry.Current;
                if (player == null)
                {
                    PlayerInputHandler fallback = FindFirstObjectByType<PlayerInputHandler>();
                    player = fallback != null ? fallback.gameObject : null;
                }

                BindPlayer(player);
            }

            MenuRoot.SetActive(false);

            LookSensitivitySlider.value = m_PlayerInputsHandler != null
                ? m_PlayerInputsHandler.LookSensitivity
                : 1f;
            LookSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);

            ShadowsToggle.isOn = QualitySettings.shadows != ShadowQuality.Disable;
            ShadowsToggle.onValueChanged.AddListener(OnShadowsChanged);

            InvincibilityToggle.isOn = m_PlayerHealth != null && m_PlayerHealth.Invincible;
            InvincibilityToggle.onValueChanged.AddListener(OnInvincibilityChanged);

            m_SubmitAction = InputSystem.actions.FindAction("UI/Submit");
            m_CancelAction = InputSystem.actions.FindAction("UI/Cancel");
            m_NavigateAction = InputSystem.actions.FindAction("UI/Navigate");
            m_MenuAction = InputSystem.actions.FindAction("UI/Menu");
            
            m_SubmitAction.Enable();
            m_CancelAction.Enable();
            m_NavigateAction.Enable();
            m_MenuAction.Enable();
        }

        void OnLocalPlayerChanged(GameObject localPlayer)
        {
            BindPlayer(localPlayer);
        }

        void BindPlayer(GameObject player)
        {
            m_PlayerInputsHandler = player != null ? player.GetComponent<PlayerInputHandler>() : null;
            m_PlayerHealth = player != null ? player.GetComponent<Health>() : null;

            if (m_PlayerInputsHandler != null && LookSensitivitySlider != null)
            {
                LookSensitivitySlider.SetValueWithoutNotify(m_PlayerInputsHandler.LookSensitivity);
            }

            if (m_PlayerHealth != null && InvincibilityToggle != null)
            {
                InvincibilityToggle.SetIsOnWithoutNotify(m_PlayerHealth.Invincible);
            }
        }

        void Update()
        {
            bool forceCursorUnlocked = CursorUnlockRequest.IsActive;

            if (forceCursorUnlocked && !MenuRoot.activeSelf)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // Lock cursor when clicking outside of menu
            if (!forceCursorUnlocked && !MenuRoot.activeSelf && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (m_MenuAction.WasPressedThisFrame()
                || (MenuRoot.activeSelf && m_CancelAction.WasPressedThisFrame()))
            {
                if (ControlImage.activeSelf)
                {
                    ControlImage.SetActive(false);
                    return;
                }

                SetPauseMenuActivation(!MenuRoot.activeSelf);

            }

            if (m_NavigateAction.ReadValue<Vector2>().y != 0)
            {
                if (EventSystem.current.currentSelectedGameObject == null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                    LookSensitivitySlider.Select();
                }
            }
        }

        public void ClosePauseMenu()
        {
            SetPauseMenuActivation(false);
        }

        void SetPauseMenuActivation(bool active)
        {
            MenuRoot.SetActive(active);

            if (MenuRoot.activeSelf)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Time.timeScale = 0f;
                AudioUtility.SetMasterVolume(VolumeWhenMenuOpen);

                EventSystem.current.SetSelectedGameObject(null);
            }
            else
            {
                bool forceCursorUnlocked = CursorUnlockRequest.IsActive;
                Cursor.lockState = forceCursorUnlocked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = forceCursorUnlocked;
                Time.timeScale = 1f;
                AudioUtility.SetMasterVolume(1);
            }

        }

        void OnMouseSensitivityChanged(float newValue)
        {
            if (m_PlayerInputsHandler != null)
            {
                m_PlayerInputsHandler.LookSensitivity = newValue;
            }
        }

        void OnShadowsChanged(bool newValue)
        {
            QualitySettings.shadows = newValue ? ShadowQuality.All : ShadowQuality.Disable;
        }

        void OnInvincibilityChanged(bool newValue)
        {
            if (m_PlayerHealth != null)
            {
                m_PlayerHealth.Invincible = newValue;
            }
        }

        public void OnShowControlButtonClicked(bool show)
        {
            ControlImage.SetActive(show);
        }
    }
}


