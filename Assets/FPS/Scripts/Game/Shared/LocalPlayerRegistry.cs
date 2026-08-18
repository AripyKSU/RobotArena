using System;
using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>Exposes the locally owned player without coupling UI to the networking assembly.</summary>
    public static class LocalPlayerRegistry
    {
        public static GameObject Current { get; private set; }
        public static event Action<GameObject> Changed;

        public static void Set(GameObject player)
        {
            if (Current == player)
            {
                return;
            }

            Current = player;
            Changed?.Invoke(Current);
        }

        public static void Clear(GameObject player)
        {
            if (Current != player)
            {
                return;
            }

            Current = null;
            Changed?.Invoke(null);
        }
    }
}
