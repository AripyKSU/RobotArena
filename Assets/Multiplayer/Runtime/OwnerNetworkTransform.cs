using Unity.Netcode.Components;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// The owning client simulates the current FPS character controller and publishes its transform.
    /// Server-side hit validation will be added separately, before combat is networked.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
