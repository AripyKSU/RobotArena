using System;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    /// <summary>Server-only signal raised after every expected PlayerObject exists.</summary>
    public static class ServerGameplayReadySignal
    {
        public static event Action Raised;
        public static event Action ResetPerformed;

        public static bool IsRaised { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeStatics()
        {
            IsRaised = false;
            Raised = null;
            ResetPerformed = null;
        }

        public static void Raise()
        {
            if (IsRaised)
            {
                return;
            }

            IsRaised = true;
            Raised?.Invoke();
        }

        public static void Reset()
        {
            if (!IsRaised)
            {
                return;
            }

            IsRaised = false;
            ResetPerformed?.Invoke();
        }
    }
}
