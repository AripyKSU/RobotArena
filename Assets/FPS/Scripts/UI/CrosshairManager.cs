using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.FPS.UI
{
    public class CrosshairManager : MonoBehaviour
    {
        public Image CrosshairImage;
        public Sprite NullCrosshairSprite;
        public float CrosshairUpdateshrpness = 5f;

        PlayerWeaponsManager m_WeaponsManager;
        bool m_WasPointingAtEnemy;
        RectTransform m_CrosshairRectTransform;
        CrosshairData m_CrosshairDataDefault;
        CrosshairData m_CrosshairDataTarget;
        CrosshairData m_CurrentCrosshair;

        void OnEnable()
        {
            LocalPlayerRegistry.Changed += OnLocalPlayerChanged;
            if (LocalPlayerRegistry.Current != null)
            {
                BindWeaponsManager(LocalPlayerRegistry.Current.GetComponent<PlayerWeaponsManager>());
            }
        }

        void Start()
        {
            if (m_WeaponsManager == null)
            {
                PlayerWeaponsManager manager = LocalPlayerRegistry.Current != null
                    ? LocalPlayerRegistry.Current.GetComponent<PlayerWeaponsManager>()
                    : FindFirstObjectByType<PlayerWeaponsManager>();
                BindWeaponsManager(manager);
            }
        }

        void OnDisable()
        {
            LocalPlayerRegistry.Changed -= OnLocalPlayerChanged;
            BindWeaponsManager(null);
        }

        void OnLocalPlayerChanged(GameObject localPlayer)
        {
            BindWeaponsManager(localPlayer != null
                ? localPlayer.GetComponent<PlayerWeaponsManager>()
                : null);
        }

        void BindWeaponsManager(PlayerWeaponsManager manager)
        {
            if (m_WeaponsManager == manager)
            {
                return;
            }

            if (m_WeaponsManager != null)
            {
                m_WeaponsManager.OnSwitchedToWeapon -= OnWeaponChanged;
            }

            m_WeaponsManager = manager;
            if (m_WeaponsManager == null)
            {
                CrosshairImage.enabled = false;
                return;
            }

            OnWeaponChanged(m_WeaponsManager.GetActiveWeapon());
            m_WeaponsManager.OnSwitchedToWeapon += OnWeaponChanged;
        }

        void Update()
        {
            if (m_WeaponsManager == null)
            {
                return;
            }

            UpdateCrosshairPointingAtEnemy(false);
            m_WasPointingAtEnemy = m_WeaponsManager.IsPointingAtEnemy;
        }

        void UpdateCrosshairPointingAtEnemy(bool force)
        {
            if (m_CrosshairDataDefault.CrosshairSprite == null)
                return;

            if ((force || !m_WasPointingAtEnemy) && m_WeaponsManager.IsPointingAtEnemy)
            {
                m_CurrentCrosshair = m_CrosshairDataTarget;
                CrosshairImage.sprite = m_CurrentCrosshair.CrosshairSprite;
                m_CrosshairRectTransform.sizeDelta = m_CurrentCrosshair.CrosshairSize * Vector2.one;
            }
            else if ((force || m_WasPointingAtEnemy) && !m_WeaponsManager.IsPointingAtEnemy)
            {
                m_CurrentCrosshair = m_CrosshairDataDefault;
                CrosshairImage.sprite = m_CurrentCrosshair.CrosshairSprite;
                m_CrosshairRectTransform.sizeDelta = m_CurrentCrosshair.CrosshairSize * Vector2.one;
            }

            CrosshairImage.color = Color.Lerp(CrosshairImage.color, m_CurrentCrosshair.CrosshairColor,
                Time.deltaTime * CrosshairUpdateshrpness);

            m_CrosshairRectTransform.sizeDelta = Mathf.Lerp(m_CrosshairRectTransform.sizeDelta.x,
                m_CurrentCrosshair.CrosshairSize,
                Time.deltaTime * CrosshairUpdateshrpness) * Vector2.one;
        }

        void OnWeaponChanged(WeaponController newWeapon)
        {
            if (newWeapon)
            {
                CrosshairImage.enabled = true;
                m_CrosshairDataDefault = newWeapon.CrosshairDataDefault;
                m_CrosshairDataTarget = newWeapon.CrosshairDataTargetInSight;
                m_CrosshairRectTransform = CrosshairImage.GetComponent<RectTransform>();
                DebugUtility.HandleErrorIfNullGetComponent<RectTransform, CrosshairManager>(m_CrosshairRectTransform,
                    this, CrosshairImage.gameObject);
            }
            else
            {
                if (NullCrosshairSprite)
                {
                    CrosshairImage.sprite = NullCrosshairSprite;
                }
                else
                {
                    CrosshairImage.enabled = false;
                }
            }

            UpdateCrosshairPointingAtEnemy(true);
        }
    }
}
