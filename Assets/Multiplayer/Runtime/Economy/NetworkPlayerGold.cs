using System;
using FPSBasic.Multiplayer.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer.Economy
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayerGold : NetworkBehaviour
    {
        [SerializeField, Min(0)] private int initialGold;

        public readonly NetworkVariable<int> Gold = new(
            0,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        public event Action<int> GoldChanged;

        private bool _isSubscribedToKills;

        public int CurrentGold => Gold.Value;

        public override void OnNetworkSpawn()
        {
            Gold.OnValueChanged += HandleGoldValueChanged;

            if (IsServer)
            {
                Gold.Value = Mathf.Max(0, initialGold);
                NetworkEnemyLifecycle.AnyServerKilledByPlayer += HandleEnemyKilled;
                _isSubscribedToKills = true;
            }

            GoldChanged?.Invoke(Gold.Value);
        }

        public override void OnNetworkDespawn()
        {
            Gold.OnValueChanged -= HandleGoldValueChanged;
            UnsubscribeFromKills();
        }

        public bool ServerTrySpend(int amount)
        {
            if (!IsServer || amount <= 0 || Gold.Value < amount)
            {
                return false;
            }

            Gold.Value -= amount;
            return true;
        }

        public void ServerAddGold(int amount)
        {
            if (!IsServer || amount <= 0)
            {
                return;
            }

            long nextValue = (long)Gold.Value + amount;
            Gold.Value = (int)Math.Min(nextValue, int.MaxValue);
        }

        private void HandleEnemyKilled(NetworkEnemyLifecycle enemy, ulong killerClientId)
        {
            if (!IsServer || killerClientId != OwnerClientId || enemy == null)
            {
                return;
            }

            if (!enemy.TryGetComponent(out EnemyGoldReward reward) || reward.Amount <= 0)
            {
                return;
            }

            ServerAddGold(reward.Amount);
        }

        private void HandleGoldValueChanged(int previousValue, int currentValue)
        {
            GoldChanged?.Invoke(currentValue);
        }

        private void UnsubscribeFromKills()
        {
            if (!_isSubscribedToKills)
            {
                return;
            }

            NetworkEnemyLifecycle.AnyServerKilledByPlayer -= HandleEnemyKilled;
            _isSubscribedToKills = false;
        }

        private void OnDestroy()
        {
            UnsubscribeFromKills();
        }
    }
}
