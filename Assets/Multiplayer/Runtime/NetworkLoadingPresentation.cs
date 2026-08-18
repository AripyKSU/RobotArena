using Unity.FPS.Game;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    /// <summary>Keeps a loading camera and message visible until the owned player is spawned.</summary>
    public sealed class NetworkLoadingPresentation : MonoBehaviour
    {
        [SerializeField] private Camera loadingCamera;
        [SerializeField] private AudioListener loadingAudioListener;
        [SerializeField] private GameObject loadingUiRoot;
        [SerializeField] private GameObject[] gameplayRoots;

        private void Awake()
        {
            LocalPlayerRegistry.Changed += HandleLocalPlayerChanged;
            ApplyState(LocalPlayerRegistry.Current != null);
        }

        private void OnDestroy()
        {
            LocalPlayerRegistry.Changed -= HandleLocalPlayerChanged;
        }

        private void HandleLocalPlayerChanged(GameObject localPlayer)
        {
            ApplyState(localPlayer != null);
        }

        private void ApplyState(bool playerReady)
        {
            if (loadingCamera != null)
            {
                loadingCamera.enabled = !playerReady;
            }

            if (loadingAudioListener != null)
            {
                loadingAudioListener.enabled = !playerReady;
            }

            if (loadingUiRoot != null)
            {
                loadingUiRoot.SetActive(!playerReady);
            }

            if (gameplayRoots == null)
            {
                return;
            }

            foreach (GameObject gameplayRoot in gameplayRoots)
            {
                if (gameplayRoot != null)
                {
                    gameplayRoot.SetActive(playerReady);
                }
            }
        }
    }
}
