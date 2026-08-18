using Unity.Netcode;
using UnityEngine;

namespace FPSBasic.Multiplayer
{
    /// <summary>Synchronizes KyleCharacter's current isWalking parameter.</summary>
    public sealed class NetworkKyleAnimator : NetworkBehaviour
    {
        private static readonly int IsWalking = Animator.StringToHash("isWalking");
        [SerializeField] private Animator kyleAnimator;
        [SerializeField, Min(0f)] private float walkSpeedThreshold = 0.1f;
        private readonly NetworkVariable<bool> _isWalking = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private Vector3 _lastPosition;

        public override void OnNetworkSpawn()
        {
            _lastPosition = transform.position;
            _isWalking.OnValueChanged += OnWalkingChanged;
            SetWalking(_isWalking.Value);
        }

        public override void OnNetworkDespawn() => _isWalking.OnValueChanged -= OnWalkingChanged;

        private void Update()
        {
            if (!IsOwner) return;
            float speed = Time.deltaTime > 0f ? Vector3.Distance(transform.position, _lastPosition) / Time.deltaTime : 0f;
            _lastPosition = transform.position;
            bool walking = speed > walkSpeedThreshold;
            if (walking != _isWalking.Value) _isWalking.Value = walking;
        }

        private void OnWalkingChanged(bool previousValue, bool currentValue) => SetWalking(currentValue);
        private void SetWalking(bool walking)
        {
            if (kyleAnimator != null) kyleAnimator.SetBool(IsWalking, walking);
        }
    }
}
