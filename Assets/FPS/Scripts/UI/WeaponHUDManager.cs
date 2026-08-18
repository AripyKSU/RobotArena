using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;

namespace Unity.FPS.UI
{
    public class WeaponHUDManager : MonoBehaviour
    {
        [Tooltip("UI panel containing the layoutGroup for displaying weapon ammo")]
        public RectTransform AmmoPanel;

        [Tooltip("Prefab for displaying weapon ammo")]
        public GameObject AmmoCounterPrefab;

        PlayerWeaponsManager m_PlayerWeaponsManager;
        List<AmmoCounter> m_AmmoCounters = new List<AmmoCounter>();

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
            PlayerWeaponsManager preferredManager = LocalPlayerRegistry.Current != null
                ? LocalPlayerRegistry.Current.GetComponent<PlayerWeaponsManager>()
                : FindFirstObjectByType<PlayerWeaponsManager>();
            BindPlayerWeapons(preferredManager);
        }

        void OnDisable()
        {
            LocalPlayerRegistry.Changed -= OnLocalPlayerChanged;
            UnbindPlayerWeapons();
        }

        void OnLocalPlayerChanged(GameObject localPlayer)
        {
            BindPlayerWeapons(localPlayer != null ? localPlayer.GetComponent<PlayerWeaponsManager>() : null);
        }

        void BindPlayerWeapons(PlayerWeaponsManager weaponsManager)
        {
            if (m_PlayerWeaponsManager == weaponsManager)
            {
                return;
            }

            UnbindPlayerWeapons();
            m_PlayerWeaponsManager = weaponsManager;
            if (m_PlayerWeaponsManager == null)
            {
                return;
            }

            m_PlayerWeaponsManager.OnAddedWeapon += AddWeapon;
            m_PlayerWeaponsManager.OnRemovedWeapon += RemoveWeapon;
            m_PlayerWeaponsManager.OnSwitchedToWeapon += ChangeWeapon;

            for (int i = 0; i < m_PlayerWeaponsManager.StartingWeapons.Count; i++)
            {
                WeaponController weapon = m_PlayerWeaponsManager.GetWeaponAtSlotIndex(i);
                if (weapon != null)
                {
                    AddWeapon(weapon, i);
                }
            }

            WeaponController activeWeapon = m_PlayerWeaponsManager.GetActiveWeapon();
            if (activeWeapon)
            {
                AddWeapon(activeWeapon, m_PlayerWeaponsManager.ActiveWeaponIndex);
                ChangeWeapon(activeWeapon);
            }
        }

        void UnbindPlayerWeapons()
        {
            if (m_PlayerWeaponsManager != null)
            {
                m_PlayerWeaponsManager.OnAddedWeapon -= AddWeapon;
                m_PlayerWeaponsManager.OnRemovedWeapon -= RemoveWeapon;
                m_PlayerWeaponsManager.OnSwitchedToWeapon -= ChangeWeapon;
            }

            foreach (AmmoCounter counter in m_AmmoCounters)
            {
                if (counter != null)
                {
                    Destroy(counter.gameObject);
                }
            }

            m_AmmoCounters.Clear();
            m_PlayerWeaponsManager = null;
        }

        void AddWeapon(WeaponController newWeapon, int weaponIndex)
        {
            if (newWeapon == null || m_AmmoCounters.Exists(counter =>
                    counter != null && counter.WeaponCounterIndex == weaponIndex))
            {
                return;
            }

            GameObject ammoCounterInstance = Instantiate(AmmoCounterPrefab, AmmoPanel);
            AmmoCounter newAmmoCounter = ammoCounterInstance.GetComponent<AmmoCounter>();
            DebugUtility.HandleErrorIfNullGetComponent<AmmoCounter, WeaponHUDManager>(newAmmoCounter, this,
                ammoCounterInstance.gameObject);

            newAmmoCounter.Initialize(newWeapon, weaponIndex, m_PlayerWeaponsManager);

            m_AmmoCounters.Add(newAmmoCounter);
        }

        void RemoveWeapon(WeaponController newWeapon, int weaponIndex)
        {
            int foundCounterIndex = -1;
            for (int i = 0; i < m_AmmoCounters.Count; i++)
            {
                if (m_AmmoCounters[i].WeaponCounterIndex == weaponIndex)
                {
                    foundCounterIndex = i;
                    Destroy(m_AmmoCounters[i].gameObject);
                }
            }

            if (foundCounterIndex >= 0)
            {
                m_AmmoCounters.RemoveAt(foundCounterIndex);
            }
        }

        void ChangeWeapon(WeaponController weapon)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(AmmoPanel);
        }
    }
}
