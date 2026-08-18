using Unity.Netcode;
using Unity.FPS.Game;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    /// <summary>
    /// Splits an FPS player prefab into owner-only gameplay and remote-only third-person presentation.
    /// The NetworkObject itself stays on the Player root, so CharacterController motion is synchronized.
    /// </summary>
    public sealed class NetworkPlayerPresentation : NetworkBehaviour
    {
        [SerializeField] private GameObject remoteVisualRoot;

        private Actor _actor;
        private ActorsManager _actorsManager;

        private void Awake()
        {
            _actor = GetComponent<Actor>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                RegisterServerActor();
            }

            ApplyPresentation(IsOwner);

            int enabledCameraCount = 0;
            foreach (Camera cameraComponent in GetComponentsInChildren<Camera>(true))
            {
                if (cameraComponent.enabled && cameraComponent.gameObject.activeInHierarchy)
                {
                    enabledCameraCount++;
                }
            }

            Debug.Log(
                $"[Presentation] Spawned PlayerObject={name} OwnerClientId={OwnerClientId} " +
                $"IsOwner={IsOwner} EnabledCameras={enabledCameraCount}");

            if (IsOwner && enabledCameraCount == 0)
            {
                Debug.LogError($"[Presentation] Owned PlayerObject '{name}' has no enabled camera.");
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && _actorsManager != null && _actor != null)
            {
                _actorsManager.Actors.Remove(_actor);
            }

            _actorsManager = null;
        }

        public override void OnGainedOwnership()
        {
            ApplyPresentation(true);
        }

        public override void OnLostOwnership()
        {
            ApplyPresentation(false);
        }

        private void ApplyPresentation(bool isLocalOwner)
        {
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                // Server-side enemy perception depends on every network player registering its
                // Actor with ActorsManager. OnNetworkSpawn runs before Start, so disabling Actor
                // here would prevent remote players from ever entering the detection list.
                if (behaviour is Actor)
                {
                    behaviour.enabled = true;
                    continue;
                }

                if (behaviour != this && behaviour is not NetworkBehaviour)
                {
                    behaviour.enabled = isLocalOwner;
                }
            }

            foreach (CharacterController controller in GetComponentsInChildren<CharacterController>(true))
            {
                controller.enabled = isLocalOwner;
            }

            foreach (Camera cameraComponent in GetComponentsInChildren<Camera>(true))
            {
                cameraComponent.enabled = isLocalOwner;
            }

            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
            {
                listener.enabled = isLocalOwner;
            }

            if (remoteVisualRoot != null)
            {
                remoteVisualRoot.SetActive(!isLocalOwner);
            }
        }

        private void RegisterServerActor()
        {
            if (_actor == null)
            {
                Debug.LogError($"[Presentation] PlayerObject '{name}' has no Actor component.", this);
                return;
            }

            _actorsManager = FindFirstObjectByType<ActorsManager>();
            if (_actorsManager == null)
            {
                Debug.LogError(
                    $"[Presentation] Cannot register server Actor for PlayerObject '{name}': " +
                    "ActorsManager was not found.",
                    this);
                return;
            }

            if (!_actorsManager.Actors.Contains(_actor))
            {
                _actorsManager.Actors.Add(_actor);
            }

            Debug.Log(
                $"[Presentation] Server Actor registered. PlayerObject={name} " +
                $"OwnerClientId={OwnerClientId} Actors={_actorsManager.Actors.Count}");
        }
    }
}
