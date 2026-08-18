using UnityEngine;

namespace FPSBasic.Multiplayer.Enemies
{
    [DisallowMultipleComponent]
    public sealed class EnemyGoldReward : MonoBehaviour
    {
        [SerializeField, Min(0)] private int amount = 3;

        public int Amount => amount;
    }
}
