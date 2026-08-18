using System.Collections;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;

namespace FPSBasic.Multiplayer.Weapons
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerWeaponLoadout))]
    [RequireComponent(typeof(PlayerWeaponsManager))]
    public sealed class PlayerWeaponLoadoutPresenter : MonoBehaviour
    {
        private NetworkPlayerWeaponLoadout _loadout;
        private PlayerWeaponsManager _weaponsManager;
        private bool _ready;

        private void Awake()
        {
            _loadout = GetComponent<NetworkPlayerWeaponLoadout>();
            _weaponsManager = GetComponent<PlayerWeaponsManager>();
        }

        private IEnumerator Start()
        {
            if (_loadout != null)
            {
                _loadout.StateChanged += HandleLoadoutStateChanged;
            }

            yield return null;
            _ready = true;
            ApplySelectedWeapon();
        }

        private void OnDestroy()
        {
            if (_loadout != null)
            {
                _loadout.StateChanged -= HandleLoadoutStateChanged;
            }
        }

        private void HandleLoadoutStateChanged()
        {
            if (_ready)
            {
                ApplySelectedWeapon();
            }
        }

        private void ApplySelectedWeapon()
        {
            if (_loadout == null || !_loadout.IsOwner || _weaponsManager == null)
            {
                return;
            }

            WeaponController prefab = _loadout.GetPrefab(_loadout.SelectedWeapon.Value);
            if (prefab == null)
            {
                Debug.LogError($"[Weapons] Missing prefab for {_loadout.SelectedWeapon.Value}.");
                return;
            }

            _weaponsManager.EquipExclusiveWeapon(prefab);
        }
    }
}

