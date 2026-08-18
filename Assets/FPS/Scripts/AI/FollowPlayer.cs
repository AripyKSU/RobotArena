using Unity.FPS.Game;
using UnityEngine;

namespace Unity.FPS.AI
{
    public class FollowPlayer : MonoBehaviour
    {
        Transform m_PlayerTransform;
        Vector3 m_OriginalOffset;

        void Start()
        {
            TryBindPlayer();
        }

        void LateUpdate()
        {
            // A network player can replace the scene's offline Player after MainScene has
            // loaded.  Reacquire the current actor instead of holding a destroyed transform.
            if (m_PlayerTransform == null)
            {
                if (!TryBindPlayer())
                    return;
            }

            transform.position = m_PlayerTransform.position + m_OriginalOffset;
        }

        bool TryBindPlayer()
        {
            GameObject player = LocalPlayerRegistry.Current;
            if (player == null)
            {
                ActorsManager actorsManager = FindAnyObjectByType<ActorsManager>();
                player = actorsManager != null ? actorsManager.Player : null;
            }

            if (player == null)
                return false;

            m_PlayerTransform = player.transform;
            m_OriginalOffset = transform.position - m_PlayerTransform.position;
            return true;
        }
    }
}
