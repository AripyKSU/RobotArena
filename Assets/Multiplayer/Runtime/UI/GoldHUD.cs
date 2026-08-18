using FPSBasic.Multiplayer.Economy;
using TMPro;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.UI;

namespace FPSBasic.Multiplayer.UI
{
    [DisallowMultipleComponent]
    public sealed class GoldHUD : MonoBehaviour
    {
        [SerializeField] private Image donIcon;
        [SerializeField] private TMP_Text goldText;

        private NetworkPlayerGold _boundWallet;

        private void OnEnable()
        {
            LocalPlayerRegistry.Changed += HandleLocalPlayerChanged;
            Bind(LocalPlayerRegistry.Current);
        }

        private void OnDisable()
        {
            LocalPlayerRegistry.Changed -= HandleLocalPlayerChanged;
            Unbind();
        }

        private void HandleLocalPlayerChanged(GameObject player)
        {
            Bind(player);
        }

        private void Bind(GameObject player)
        {
            Unbind();

            if (player != null)
            {
                _boundWallet = player.GetComponent<NetworkPlayerGold>();
            }

            if (_boundWallet == null)
            {
                SetGold(0);
                return;
            }

            _boundWallet.GoldChanged += HandleGoldChanged;
            SetGold(_boundWallet.CurrentGold);
        }

        private void Unbind()
        {
            if (_boundWallet != null)
            {
                _boundWallet.GoldChanged -= HandleGoldChanged;
                _boundWallet = null;
            }
        }

        private void HandleGoldChanged(int currentGold)
        {
            SetGold(currentGold);
        }

        private void SetGold(int value)
        {
            if (goldText != null)
            {
                goldText.text = Mathf.Max(0, value).ToString();
            }

            if (donIcon != null && !donIcon.gameObject.activeSelf)
            {
                donIcon.gameObject.SetActive(true);
            }
        }
    }
}
