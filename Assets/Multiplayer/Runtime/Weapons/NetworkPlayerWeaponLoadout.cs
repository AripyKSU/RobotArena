using System;
using FPSBasic.Multiplayer.Economy;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer.Weapons
{
    public enum WeaponId : byte
    {
        Blaster = 0,
        Launcher = 1,
        Shotgun = 2
    }

    public enum WeaponSelectionResult : byte
    {
        Success = 0,
        InsufficientGold = 1,
        InvalidWeapon = 2,
        NotRespawning = 3
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkPlayerGold))]
    [RequireComponent(typeof(NetworkPlayerHealth))]
    public sealed class NetworkPlayerWeaponLoadout : NetworkBehaviour
    {
        private const byte BlasterMask = 1 << (int)WeaponId.Blaster;

        [Header("Weapon Prefabs")]
        [SerializeField] private WeaponController blasterPrefab;
        [SerializeField] private WeaponController launcherPrefab;
        [SerializeField] private WeaponController shotgunPrefab;

        public readonly NetworkVariable<byte> OwnedWeapons = new(
            BlasterMask,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<WeaponId> SelectedWeapon = new(
            WeaponId.Blaster,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        private NetworkPlayerGold _wallet;
        private NetworkPlayerHealth _health;

        public event Action StateChanged;
        public event Action<WeaponSelectionResult> SelectionResultReceived;

        private void Awake()
        {
            _wallet = GetComponent<NetworkPlayerGold>();
            _health = GetComponent<NetworkPlayerHealth>();
        }

        public override void OnNetworkSpawn()
        {
            OwnedWeapons.OnValueChanged += HandleOwnedWeaponsChanged;
            SelectedWeapon.OnValueChanged += HandleSelectedWeaponChanged;

            if (IsServer)
            {
                OwnedWeapons.Value = BlasterMask;
                SelectedWeapon.Value = WeaponId.Blaster;
            }

            StateChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            OwnedWeapons.OnValueChanged -= HandleOwnedWeaponsChanged;
            SelectedWeapon.OnValueChanged -= HandleSelectedWeaponChanged;
        }

        public bool IsOwned(WeaponId weaponId)
        {
            return IsValidWeapon(weaponId) &&
                   (OwnedWeapons.Value & GetMask(weaponId)) != 0;
        }

        public int GetPrice(WeaponId weaponId)
        {
            return weaponId switch
            {
                WeaponId.Blaster => 0,
                WeaponId.Launcher => 15,
                WeaponId.Shotgun => 30,
                _ => int.MaxValue
            };
        }

        public WeaponController GetPrefab(WeaponId weaponId)
        {
            return weaponId switch
            {
                WeaponId.Blaster => blasterPrefab,
                WeaponId.Launcher => launcherPrefab,
                WeaponId.Shotgun => shotgunPrefab,
                _ => null
            };
        }

        public void RequestSelectOrPurchase(WeaponId weaponId)
        {
            if (!IsOwner || !IsSpawned)
            {
                return;
            }

            RequestSelectOrPurchaseServerRpc(weaponId);
        }

        [ServerRpc]
        private void RequestSelectOrPurchaseServerRpc(WeaponId weaponId)
        {
            if (!IsValidWeapon(weaponId) || GetPrefab(weaponId) == null)
            {
                SendSelectionResult(WeaponSelectionResult.InvalidWeapon);
                return;
            }

            if (_health == null || !_health.IsRespawning)
            {
                SendSelectionResult(WeaponSelectionResult.NotRespawning);
                return;
            }

            if (!IsOwned(weaponId))
            {
                int price = GetPrice(weaponId);
                if (_wallet == null || !_wallet.ServerTrySpend(price))
                {
                    SendSelectionResult(WeaponSelectionResult.InsufficientGold);
                    return;
                }

                OwnedWeapons.Value = (byte)(OwnedWeapons.Value | GetMask(weaponId));
            }

            SelectedWeapon.Value = weaponId;
            SendSelectionResult(WeaponSelectionResult.Success);
        }

        private void SendSelectionResult(WeaponSelectionResult result)
        {
            SelectionResultClientRpc(
                result,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { OwnerClientId }
                    }
                });
        }

        [ClientRpc]
        private void SelectionResultClientRpc(
            WeaponSelectionResult result,
            ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
            {
                SelectionResultReceived?.Invoke(result);
            }
        }

        private void HandleOwnedWeaponsChanged(byte previousValue, byte currentValue)
        {
            StateChanged?.Invoke();
        }

        private void HandleSelectedWeaponChanged(WeaponId previousValue, WeaponId currentValue)
        {
            StateChanged?.Invoke();
        }

        private static bool IsValidWeapon(WeaponId weaponId)
        {
            return weaponId is WeaponId.Blaster or WeaponId.Launcher or WeaponId.Shotgun;
        }

        private static byte GetMask(WeaponId weaponId)
        {
            return (byte)(1 << (int)weaponId);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            blasterPrefab = LoadWeapon("Assets/FPS/Prefabs/Weapons/Weapon_Blaster.prefab");
            launcherPrefab = LoadWeapon("Assets/FPS/Prefabs/Weapons/Weapon_Launcher.prefab");
            shotgunPrefab = LoadWeapon("Assets/FPS/Prefabs/Weapons/Weapon_Shotgun.prefab");
        }

        private static WeaponController LoadWeapon(string assetPath)
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            return prefab != null ? prefab.GetComponent<WeaponController>() : null;
        }
#endif
    }
}
