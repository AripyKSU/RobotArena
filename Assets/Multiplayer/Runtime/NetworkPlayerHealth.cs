using System.Collections;
using FPSBasic.Multiplayer.Economy;
using FPSBasic.Multiplayer.Weapons;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.FPS.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace FPSBasic.Multiplayer
{
    [RequireComponent(typeof(Health))]
    public sealed class NetworkPlayerHealth : NetworkBehaviour
    {
        private static readonly int IsDeadAnimatorParameter = Animator.StringToHash("isDead");
        private static readonly int IsWalkingAnimatorParameter = Animator.StringToHash("isWalking");

        public NetworkVariable<float> CurrentHealth = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _isRespawning = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<double> _respawnEndsAt = new(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [SerializeField, Min(0.1f)] private float respawnDuration = 5f;

        private Health _health;
        private PlayerCharacterController _characterController;
        private PlayerInputHandler _inputHandler;
        private PlayerWeaponsManager _weaponsManager;
        private CharacterController _motor;
        private OwnerNetworkTransform _networkTransform;
        private Animator _thirdPersonAnimator;
        private Vector3 _visualLocalPosition;
        private Quaternion _visualLocalRotation;
        private Vector3 _visualLocalScale;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Coroutine _respawnRoutine;
        private GameObject _respawnCanvas;
        private Image _respawnFill;
        private Texture2D _respawnTexture;
        private Sprite _respawnSprite;
        private float _localRespawnStartedAt;
        private bool _respawnStateApplied;
        private ulong _lastValidAttackerClientId;
        private bool _hasLastValidAttacker;
        private bool _deathScoreReported;
        private bool _matchFinished;

        public static NetworkPlayerHealth LocalPlayer { get; private set; }
        public Health PlayerHealth => _health;
        public bool IsRespawning => _isRespawning.Value;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _characterController = GetComponent<PlayerCharacterController>();
            _inputHandler = GetComponent<PlayerInputHandler>();
            _weaponsManager = GetComponent<PlayerWeaponsManager>();
            _motor = GetComponent<CharacterController>();
            _networkTransform = GetComponent<OwnerNetworkTransform>();
            _thirdPersonAnimator = GetComponentInChildren<Animator>(true);

            if (_thirdPersonAnimator != null)
            {
                _thirdPersonAnimator.applyRootMotion = false;
                Transform visualTransform = _thirdPersonAnimator.transform;
                _visualLocalPosition = visualTransform.localPosition;
                _visualLocalRotation = visualTransform.localRotation;
                _visualLocalScale = visualTransform.localScale;
            }
        }

        public override void OnNetworkSpawn()
        {
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            CurrentHealth.OnValueChanged += OnHealthChanged;
            _isRespawning.OnValueChanged += OnRespawningChanged;
            NetworkMatchController.LocalMatchFinished += OnLocalMatchFinished;

            if (IsOwner)
            {
                LocalPlayer = this;
                LocalPlayerRegistry.Set(gameObject);
                PlayerHealthBar.SetLocalPlayerHealth(_health);
            }

            if (IsServer)
            {
                _health.Revive();
                CurrentHealth.Value = _health.CurrentHealth;
                _health.OnDamaged += OnServerDamaged;
                _health.OnDie += OnServerDied;
            }
            else if (CurrentHealth.Value > 0f)
            {
                _health.ApplyNetworkState(CurrentHealth.Value);
            }

            ApplyRespawnState(_isRespawning.Value);

            if (NetworkMatchController.LocalInstance != null &&
                NetworkMatchController.LocalInstance.Result != MatchResult.None)
            {
                EnterMatchFinishedState();
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentHealth.OnValueChanged -= OnHealthChanged;
            _isRespawning.OnValueChanged -= OnRespawningChanged;
            NetworkMatchController.LocalMatchFinished -= OnLocalMatchFinished;

            if (IsServer)
            {
                _health.OnDamaged -= OnServerDamaged;
                _health.OnDie -= OnServerDied;
            }

            if (_respawnRoutine != null)
            {
                StopCoroutine(_respawnRoutine);
                _respawnRoutine = null;
            }

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
                LocalPlayerRegistry.Clear(gameObject);
                PlayerHealthBar.SetLocalPlayerHealth(null);
            }

            DestroyRespawnUi();
        }

        private void OnServerDamaged(float damage, GameObject damageSource)
        {
            if (IsServer && !_matchFinished)
            {
                CurrentHealth.Value = _health.CurrentHealth;
                _hasLastValidAttacker = TryResolvePlayerClientId(
                    damageSource,
                    out _lastValidAttackerClientId);
            }
        }

        private void OnServerDied()
        {
            if (!IsServer || _matchFinished || _respawnRoutine != null)
            {
                return;
            }

            if (!_deathScoreReported)
            {
                _deathScoreReported = true;
                if (_hasLastValidAttacker)
                {
                    NetworkMatchController.LocalInstance?.TryRegisterPlayerKill(
                        _lastValidAttackerClientId,
                        OwnerClientId);
                }
            }

            _respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            _health.Invincible = true;
            _respawnEndsAt.Value = NetworkManager.ServerTime.Time + respawnDuration;
            _isRespawning.Value = true;

            yield return new WaitForSecondsRealtime(respawnDuration);

            if (_matchFinished)
            {
                _respawnRoutine = null;
                yield break;
            }

            TeleportToSpawn();
            _health.Revive();
            CurrentHealth.Value = _health.CurrentHealth;
            _isRespawning.Value = false;
            _respawnEndsAt.Value = 0d;
            _hasLastValidAttacker = false;
            _lastValidAttackerClientId = default;
            _deathScoreReported = false;
            _respawnRoutine = null;
        }

        private static bool TryResolvePlayerClientId(
            GameObject damageSource,
            out ulong clientId)
        {
            if (damageSource != null)
            {
                NetworkObject networkObject = damageSource.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    networkObject = damageSource.GetComponentInParent<NetworkObject>();
                }

                if (networkObject != null && networkObject.IsPlayerObject)
                {
                    clientId = networkObject.OwnerClientId;
                    return true;
                }
            }

            clientId = default;
            return false;
        }

        private void OnHealthChanged(float previousValue, float currentValue)
        {
            if (!IsServer)
            {
                _health.ApplyNetworkState(currentValue);
            }
        }

        private void OnRespawningChanged(bool previousValue, bool currentValue)
        {
            if (_matchFinished)
            {
                return;
            }

            ApplyRespawnState(currentValue);
        }

        private void OnLocalMatchFinished(
            MatchResult result,
            int finalTeamAScore,
            int finalTeamBScore)
        {
            EnterMatchFinishedState();
        }

        private void EnterMatchFinishedState()
        {
            if (_matchFinished)
            {
                return;
            }

            _matchFinished = true;
            _health.Invincible = true;

            if (IsServer && _respawnRoutine != null)
            {
                StopCoroutine(_respawnRoutine);
                _respawnRoutine = null;
                _isRespawning.Value = false;
                _respawnEndsAt.Value = 0d;
            }

            if (_thirdPersonAnimator != null)
            {
                _thirdPersonAnimator.SetBool(IsWalkingAnimatorParameter, false);
            }

            if (IsOwner)
            {
                SetOwnerControlsEnabled(false);
                DestroyRespawnUi();
            }
        }

        private void ApplyRespawnState(bool respawning)
        {
            _health.Invincible = respawning;

            if (_thirdPersonAnimator != null)
            {
                _thirdPersonAnimator.applyRootMotion = false;
                _thirdPersonAnimator.SetBool(IsDeadAnimatorParameter, respawning);
                if (respawning)
                {
                    _thirdPersonAnimator.SetBool(IsWalkingAnimatorParameter, false);
                }
            }

            if (respawning)
            {
                _respawnStateApplied = true;
            }
            else if (!_respawnStateApplied)
            {
                return;
            }

            if (!respawning)
            {
                ResetThirdPersonVisual();
            }

            if (!IsOwner)
            {
                if (!respawning) _respawnStateApplied = false;
                return;
            }

            if (respawning)
            {
                _localRespawnStartedAt = Time.unscaledTime;
                SetOwnerControlsEnabled(false);
                CreateRespawnUi();
            }
            else
            {
                TeleportToSpawn();
                _health.Revive();
                _characterController?.Revive();
                SetOwnerControlsEnabled(true);
                DestroyRespawnUi();
                _respawnStateApplied = false;
            }
        }

        private void Update()
        {
            if (!IsOwner || !_isRespawning.Value || _respawnFill == null)
            {
                return;
            }

            float elapsed = Time.unscaledTime - _localRespawnStartedAt;
            _respawnFill.fillAmount = Mathf.Clamp01(elapsed / respawnDuration);
        }

        private void SetOwnerControlsEnabled(bool enabled)
        {
            if (!enabled && _weaponsManager != null)
            {
                _weaponsManager.SetRespawnLocked(true);
            }

            if (_inputHandler != null) _inputHandler.enabled = enabled;
            if (_characterController != null) _characterController.enabled = enabled;
            if (_weaponsManager != null) _weaponsManager.enabled = enabled;
            if (_motor != null) _motor.enabled = enabled;

            if (enabled && _weaponsManager != null)
            {
                _weaponsManager.SetRespawnLocked(false);
            }
        }

        private void TeleportToSpawn()
        {
            bool motorWasEnabled = _motor != null && _motor.enabled;
            if (_motor != null) _motor.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);

            if (IsOwner && _networkTransform != null && _networkTransform.IsSpawned)
            {
                _networkTransform.Teleport(_spawnPosition, _spawnRotation, transform.localScale);
            }

            if (_motor != null) _motor.enabled = motorWasEnabled;
        }

        private void ResetThirdPersonVisual()
        {
            if (_thirdPersonAnimator == null)
            {
                return;
            }

            Transform visualTransform = _thirdPersonAnimator.transform;
            visualTransform.SetLocalPositionAndRotation(_visualLocalPosition, _visualLocalRotation);
            visualTransform.localScale = _visualLocalScale;
            _thirdPersonAnimator.Rebind();
            if (_thirdPersonAnimator.isActiveAndEnabled)
            {
                _thirdPersonAnimator.Update(0f);
            }
            _thirdPersonAnimator.SetBool(IsDeadAnimatorParameter, false);
            _thirdPersonAnimator.SetBool(IsWalkingAnimatorParameter, false);
        }

        private void CreateRespawnUi()
        {
            if (_respawnCanvas != null)
            {
                return;
            }

            _respawnCanvas = new GameObject(
                "RespawnProgressCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            Canvas canvas = _respawnCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvas.pixelPerfect = true;

            CanvasScaler scaler = _respawnCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(_respawnCanvas.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = new Vector2(520f, 42f);

            _respawnTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "RespawnProgressTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _respawnTexture.SetPixel(0, 0, Color.white);
            _respawnTexture.Apply(false, true);
            _respawnSprite = Sprite.Create(
                _respawnTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);

            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = _respawnSprite;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.color = new Color(0f, 0f, 0f, 0.82f);
            backgroundImage.raycastTarget = false;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(backgroundObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(4f, 4f);
            fillRect.offsetMax = new Vector2(-4f, -4f);

            _respawnFill = fillObject.GetComponent<Image>();
            _respawnFill.sprite = _respawnSprite;
            _respawnFill.color = new Color(0.2f, 0.85f, 1f, 1f);
            _respawnFill.type = Image.Type.Filled;
            _respawnFill.fillMethod = Image.FillMethod.Horizontal;
            _respawnFill.fillOrigin = 0;
            _respawnFill.fillAmount = 0f;
            _respawnFill.raycastTarget = false;

            RespawnWeaponSelectionUI weaponSelectionUi =
                _respawnCanvas.AddComponent<RespawnWeaponSelectionUI>();
            weaponSelectionUi.Initialize(
                GetComponent<NetworkPlayerGold>(),
                GetComponent<NetworkPlayerWeaponLoadout>());
        }

        private void DestroyRespawnUi()
        {
            if (_respawnCanvas != null)
            {
                Destroy(_respawnCanvas);
            }

            if (_respawnSprite != null)
            {
                Destroy(_respawnSprite);
            }

            if (_respawnTexture != null)
            {
                Destroy(_respawnTexture);
            }

            _respawnCanvas = null;
            _respawnFill = null;
            _respawnSprite = null;
            _respawnTexture = null;
        }
    }
}
