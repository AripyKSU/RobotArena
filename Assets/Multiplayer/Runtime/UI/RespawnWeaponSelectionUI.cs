using System;
using System.Collections.Generic;
using FPSBasic.Multiplayer.Economy;
using TMPro;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.UI;

namespace FPSBasic.Multiplayer.Weapons
{
    [DisallowMultipleComponent]
    public sealed class RespawnWeaponSelectionUI : MonoBehaviour
    {
        private sealed class WeaponRow
        {
            public Button Button;
            public Image Background;
            public TMP_Text PriceOrState;
        }

        private readonly Dictionary<WeaponId, WeaponRow> _rows = new();

        private NetworkPlayerGold _wallet;
        private NetworkPlayerWeaponLoadout _loadout;
        private TMP_Text _message;
        private WeaponId? _pendingWeapon;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;
        private bool _initialized;

        public void Initialize(
            NetworkPlayerGold wallet,
            NetworkPlayerWeaponLoadout loadout)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _wallet = wallet;
            _loadout = loadout;

            BuildUi();

            if (_wallet != null)
            {
                _wallet.GoldChanged += HandleGoldChanged;
            }

            if (_loadout != null)
            {
                _loadout.StateChanged += Refresh;
                _loadout.SelectionResultReceived += HandleSelectionResult;
            }

            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            CursorUnlockRequest.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Refresh();
        }

        private void LateUpdate()
        {
            if (!_initialized)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            if (_wallet != null)
            {
                _wallet.GoldChanged -= HandleGoldChanged;
            }

            if (_loadout != null)
            {
                _loadout.StateChanged -= Refresh;
                _loadout.SelectionResultReceived -= HandleSelectionResult;
            }

            if (_initialized)
            {
                CursorUnlockRequest.SetActive(false);
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
            }
        }

        private void BuildUi()
        {
            GameObject panelObject = CreateUiObject("WeaponSelectionPanel", transform);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-70f, 0f);
            panelRect.sizeDelta = new Vector2(420f, 450f);

            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.025f, 0.04f, 0.065f, 0.94f);

            TMP_Text title = CreateText("Title", panelRect, "WEAPON SHOP", 30f, TextAlignmentOptions.Center);
            SetRect(title.rectTransform, new Vector2(0f, 182f), new Vector2(380f, 42f));

            CreateRow(panelRect, WeaponId.Blaster, "Blaster", 126f);
            CreateRow(panelRect, WeaponId.Shotgun, "Shotgun", 72f);
            CreateRow(panelRect, WeaponId.Launcher, "Launcher", 18f);
            CreateRow(panelRect, WeaponId.Vaporizer, "Vaporizer", -36f);
            CreateRow(panelRect, WeaponId.SniperRifle, "Sniper Rifle", -90f);
            CreateRow(panelRect, WeaponId.Fireballer, "Fireballer", -144f);

            _message = CreateText("Message", panelRect, string.Empty, 18f, TextAlignmentOptions.Center);
            _message.color = new Color(1f, 0.7f, 0.25f, 1f);
            SetRect(_message.rectTransform, new Vector2(0f, -192f), new Vector2(380f, 30f));
        }

        private void CreateRow(
            RectTransform parent,
            WeaponId weaponId,
            string displayName,
            float y)
        {
            GameObject rowObject = CreateUiObject($"{weaponId}Row", parent);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            SetRect(rowRect, new Vector2(0f, y), new Vector2(370f, 48f));

            Image background = rowObject.AddComponent<Image>();
            background.color = new Color(0.12f, 0.16f, 0.22f, 1f);

            Button button = rowObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => HandleWeaponClicked(weaponId));

            TMP_Text nameText = CreateText(
                "Name",
                rowRect,
                displayName,
                23f,
                TextAlignmentOptions.MidlineLeft);
            RectTransform nameRect = nameText.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(18f, 0f);
            nameRect.offsetMax = new Vector2(-150f, 0f);

            TMP_Text priceText = CreateText(
                "PriceOrState",
                rowRect,
                string.Empty,
                20f,
                TextAlignmentOptions.MidlineRight);
            RectTransform priceRect = priceText.rectTransform;
            priceRect.anchorMin = Vector2.zero;
            priceRect.anchorMax = Vector2.one;
            priceRect.offsetMin = new Vector2(190f, 0f);
            priceRect.offsetMax = new Vector2(-18f, 0f);

            _rows.Add(
                weaponId,
                new WeaponRow
                {
                    Button = button,
                    Background = background,
                    PriceOrState = priceText
                });
        }

        private void HandleWeaponClicked(WeaponId weaponId)
        {
            if (_loadout == null || _pendingWeapon.HasValue)
            {
                return;
            }

            _pendingWeapon = weaponId;
            _message.text = "Processing...";
            Refresh();
            _loadout.RequestSelectOrPurchase(weaponId);
        }

        private void HandleGoldChanged(int currentGold)
        {
            Refresh();
        }

        private void HandleSelectionResult(WeaponSelectionResult result)
        {
            _pendingWeapon = null;
            _message.text = result switch
            {
                WeaponSelectionResult.Success => string.Empty,
                WeaponSelectionResult.InsufficientGold => "Not enough Gold.",
                WeaponSelectionResult.NotRespawning => "Available only while respawning.",
                _ => "Unable to select this weapon."
            };
            Refresh();
        }

        private void Refresh()
        {
            int currentGold = _wallet != null ? _wallet.CurrentGold : 0;

            foreach (KeyValuePair<WeaponId, WeaponRow> pair in _rows)
            {
                WeaponId weaponId = pair.Key;
                WeaponRow row = pair.Value;
                bool owned = _loadout != null && _loadout.IsOwned(weaponId);
                bool selected = owned && _loadout.SelectedWeapon.Value == weaponId;
                int price = _loadout != null ? _loadout.GetPrice(weaponId) : int.MaxValue;
                bool affordable = owned || currentGold >= price;
                bool pending = _pendingWeapon.HasValue;

                row.Button.interactable = !pending && !selected && affordable;
                row.Background.color = selected
                    ? new Color(0.12f, 0.55f, 0.72f, 1f)
                    : affordable
                        ? new Color(0.12f, 0.16f, 0.22f, 1f)
                        : new Color(0.08f, 0.09f, 0.11f, 0.8f);

                if (selected)
                {
                    row.PriceOrState.text = "Selected";
                }
                else if (owned)
                {
                    row.PriceOrState.text = string.Empty;
                }
                else
                {
                    row.PriceOrState.text = $"{price} Gold";
                }
            }
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject uiObject = new(name, typeof(RectTransform));
            uiObject.transform.SetParent(parent, false);
            return uiObject;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string value,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = CreateUiObject(name, parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}


