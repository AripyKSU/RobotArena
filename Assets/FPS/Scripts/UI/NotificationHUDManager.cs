using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;

namespace Unity.FPS.UI
{
    public class NotificationHUDManager : MonoBehaviour
    {
        [Tooltip("UI panel containing the layoutGroup for displaying notifications")]
        public RectTransform NotificationPanel;

        [Tooltip("Prefab for the notifications")]
        public GameObject NotificationPrefab;

        PlayerWeaponsManager m_PlayerWeaponsManager;
        Jetpack m_Jetpack;

        void OnEnable()
        {
            LocalPlayerRegistry.Changed += OnLocalPlayerChanged;
            EventManager.AddListener<ObjectiveUpdateEvent>(OnObjectiveUpdateEvent);

            if (LocalPlayerRegistry.Current != null)
            {
                BindPlayer(LocalPlayerRegistry.Current);
            }
        }

        void Start()
        {
            if (m_PlayerWeaponsManager == null && LocalPlayerRegistry.Current == null)
            {
                PlayerWeaponsManager manager = FindFirstObjectByType<PlayerWeaponsManager>();
                BindPlayer(manager != null ? manager.gameObject : null);
            }
        }

        void OnDisable()
        {
            LocalPlayerRegistry.Changed -= OnLocalPlayerChanged;
            EventManager.RemoveListener<ObjectiveUpdateEvent>(OnObjectiveUpdateEvent);
            BindPlayer(null);
        }

        void OnLocalPlayerChanged(GameObject localPlayer)
        {
            BindPlayer(localPlayer);
        }

        void BindPlayer(GameObject player)
        {
            if (m_PlayerWeaponsManager != null)
            {
                m_PlayerWeaponsManager.OnAddedWeapon -= OnPickupWeapon;
            }

            if (m_Jetpack != null)
            {
                m_Jetpack.OnUnlockJetpack -= OnUnlockJetpack;
            }

            m_PlayerWeaponsManager = player != null ? player.GetComponent<PlayerWeaponsManager>() : null;
            m_Jetpack = player != null ? player.GetComponent<Jetpack>() : null;

            if (m_PlayerWeaponsManager != null)
            {
                m_PlayerWeaponsManager.OnAddedWeapon += OnPickupWeapon;
            }

            if (m_Jetpack != null)
            {
                m_Jetpack.OnUnlockJetpack += OnUnlockJetpack;
            }
        }

        void OnObjectiveUpdateEvent(ObjectiveUpdateEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.NotificationText))
                CreateNotification(evt.NotificationText);
        }

        void OnPickupWeapon(WeaponController weaponController, int index)
        {
            if (index != 0)
                CreateNotification("Picked up weapon : " + weaponController.WeaponName);
        }

        void OnUnlockJetpack(bool unlock)
        {
            CreateNotification("Jetpack unlocked");
        }

        public void CreateNotification(string text)
        {
            GameObject notificationInstance = Instantiate(NotificationPrefab, NotificationPanel);
            notificationInstance.transform.SetSiblingIndex(0);

            NotificationToast toast = notificationInstance.GetComponent<NotificationToast>();
            if (toast)
            {
                toast.Initialize(text);
            }
        }

    }
}
