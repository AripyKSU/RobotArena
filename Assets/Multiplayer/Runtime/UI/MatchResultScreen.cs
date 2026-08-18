using System;
using System.Threading.Tasks;
using TMPro;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FPSBasic.Multiplayer.UI
{
    /// <summary>
    /// Keeps the locally-rendered result visible even when the host closes the network session.
    /// The network result is copied into this non-networked overlay before NGO objects despawn.
    /// </summary>
    public sealed class MatchResultScreen : MonoBehaviour
    {
        private const string TitleSceneName = "StartScene";

        [SerializeField] private Sprite redWinSprite;
        [SerializeField] private Sprite blueWinSprite;
        [SerializeField] private Sprite drawSprite;

        private GameObject _resultCanvasObject;
        private Button _backButton;
        private bool _resultShown;
        private bool _returningToTitle;

        private void OnEnable()
        {
            NetworkMatchController.LocalMatchFinished += HandleMatchFinished;

            NetworkMatchController controller = NetworkMatchController.LocalInstance;
            if (controller != null && controller.Result != MatchResult.None)
            {
                ShowResult(controller.Result);
            }
        }

        private void OnDisable()
        {
            NetworkMatchController.LocalMatchFinished -= HandleMatchFinished;
        }

        private void OnDestroy()
        {
            if (_resultCanvasObject != null)
            {
                Destroy(_resultCanvasObject);
            }
        }

        private void HandleMatchFinished(
            MatchResult result,
            int finalTeamAScore,
            int finalTeamBScore)
        {
            ShowResult(result);
        }

        private void ShowResult(MatchResult result)
        {
            if (_resultShown || result == MatchResult.None)
            {
                return;
            }

            _resultShown = true;
            Sprite resultSprite = GetResultSprite(result);
            CreateResultCanvas(resultSprite);
            DisableGameplayCanvases();

            CursorUnlockRequest.SetActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private Sprite GetResultSprite(MatchResult result)
        {
            return result switch
            {
                MatchResult.TeamAWin => redWinSprite,
                MatchResult.TeamBWin => blueWinSprite,
                MatchResult.Draw => drawSprite,
                _ => null
            };
        }

        private void CreateResultCanvas(Sprite resultSprite)
        {
            _resultCanvasObject = new GameObject(
                "MatchResultCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            Canvas canvas = _resultCanvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            CanvasScaler scaler = _resultCanvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image backdrop = CreateImage("Backdrop", _resultCanvasObject.transform);
            Stretch(backdrop.rectTransform);
            backdrop.color = new Color(0.015f, 0.02f, 0.035f, 0.96f);

            Image resultImage = CreateImage("ResultImage", _resultCanvasObject.transform);
            RectTransform resultRect = resultImage.rectTransform;
            resultRect.anchorMin = new Vector2(0.5f, 0.5f);
            resultRect.anchorMax = new Vector2(0.5f, 0.5f);
            resultRect.pivot = new Vector2(0.5f, 0.5f);
            resultRect.anchoredPosition = new Vector2(0f, 80f);
            resultRect.sizeDelta = new Vector2(1280f, 720f);
            resultImage.sprite = resultSprite;
            resultImage.preserveAspect = true;
            resultImage.raycastTarget = false;

            GameObject buttonObject = new GameObject(
                "BackToTitleButton",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(_resultCanvasObject.transform, false);

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 75f);
            buttonRect.sizeDelta = new Vector2(440f, 92f);

            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.12f, 0.16f, 0.23f, 0.98f);

            _backButton = buttonObject.GetComponent<Button>();
            ColorBlock colors = _backButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.82f, 0.9f, 1f, 1f);
            colors.pressedColor = new Color(0.62f, 0.72f, 0.86f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
            _backButton.colors = colors;
            _backButton.onClick.AddListener(HandleBackToTitleClicked);

            GameObject labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            Stretch(label.rectTransform);
            label.text = "BACK TO TITLE";
            label.fontSize = 38f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        private void DisableGameplayCanvases()
        {
            Canvas resultCanvas = _resultCanvasObject.GetComponent<Canvas>();
            Canvas[] canvases = FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (Canvas canvas in canvases)
            {
                if (canvas != null && canvas != resultCanvas)
                {
                    canvas.enabled = false;
                }
            }
        }

        private async void HandleBackToTitleClicked()
        {
            if (_returningToTitle)
            {
                return;
            }

            _returningToTitle = true;
            if (_backButton != null)
            {
                _backButton.interactable = false;
            }

            GameObject bootstrapRoot = null;

            try
            {
                MultiplayerFlowController flow = FindFirstObjectByType<MultiplayerFlowController>();
                if (flow != null)
                {
                    bootstrapRoot = flow.transform.root.gameObject;
                    await flow.LeaveRoomAsync();
                    await WaitForFlowOperationAsync(flow);
                }

                // A host-ended-session callback may already own the flow operation.  Waiting
                // here as well prevents StartScene from loading over an NGO shutdown in flight.
                await ShutdownNetworkAndWaitAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            // MultiplayerBootstrap is DontDestroyOnLoad.  Remove the old NetworkManager and
            // flow root before StartScene creates its fresh bootstrap; otherwise the menu can
            // reference a different NetworkManager than the Sessions network module uses.
            if (bootstrapRoot != null)
            {
                Destroy(bootstrapRoot);
                await WaitForDestroyedObjectAsync(bootstrapRoot);
            }

            CursorUnlockRequest.SetActive(false);
            SceneManager.LoadScene(TitleSceneName, LoadSceneMode.Single);
        }

        private static async Task WaitForFlowOperationAsync(MultiplayerFlowController flow)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (flow != null && flow.IsOperationInProgress && DateTime.UtcNow < deadline)
            {
                await Task.Yield();
            }
        }

        private static async Task ShutdownNetworkAndWaitAsync()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                return;
            }

            if (networkManager.IsListening && !networkManager.ShutdownInProgress)
            {
                networkManager.Shutdown();
            }

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (networkManager != null &&
                   (networkManager.IsListening || networkManager.ShutdownInProgress) &&
                   DateTime.UtcNow < deadline)
            {
                await Task.Yield();
            }
        }

        private static async Task WaitForDestroyedObjectAsync(GameObject target)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (target != null && DateTime.UtcNow < deadline)
            {
                await Task.Yield();
            }
        }

        private static Image CreateImage(string name, Transform parent)
        {
            GameObject imageObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            imageObject.transform.SetParent(parent, false);
            return imageObject.GetComponent<Image>();
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
    }
}
