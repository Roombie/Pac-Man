using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// On-screen pause button for touch devices, which have no Pause key
/// Builds its own overlay canvas so no scene setup is needed. It shows from the READY intro on,
/// dimmed until play starts (pausing mid-intro would resume straight into play and skip it)
/// The Android Back key (reported as Escape) pauses too
/// </summary>
[RequireComponent(typeof(MobileOverlayCanvas))]
public class TouchPauseButton : MonoBehaviour
{
    private GameObject buttonObject;
    private CanvasGroup buttonGroup;

    public static void CreateIfTouchDevice(Transform parent)
    {
        if (Touchscreen.current == null && !TouchInput.IsMobile) return;
        if (FindAnyObjectByType<TouchPauseButton>() != null) return;

        var go = new GameObject("Touch Pause Button");
        go.transform.SetParent(parent, false);
        go.AddComponent<TouchPauseButton>();
    }

    private void Awake()
    {
        var button = GetComponent<MobileOverlayCanvas>().CreateCornerButton("Pause", 1f);
        button.onClick.AddListener(OnPressed);
        buttonObject = button.gameObject;
        buttonGroup = buttonObject.AddComponent<CanvasGroup>();

        // Two bars make the pause symbol
        var rect = (RectTransform)buttonObject.transform;
        AddBar(rect, -0.17f);
        AddBar(rect, 0.17f);
    }

    private static void AddBar(RectTransform parent, float xOffset)
    {
        var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        bar.SetParent(parent, false);
        bar.anchorMin = new Vector2(0.5f + xOffset - 0.08f, 0.25f);
        bar.anchorMax = new Vector2(0.5f + xOffset + 0.08f, 0.75f);
        bar.offsetMin = bar.offsetMax = Vector2.zero;

        var image = bar.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
    }

    private void Update()
    {
        var gm = GameManager.Instance;
        var state = gm != null ? gm.CurrentGameState : GameManager.GameState.Intro;

        bool canPause = state == GameManager.GameState.Playing;
        bool show = canPause || state == GameManager.GameState.Intro;
        if (buttonObject.activeSelf != show) buttonObject.SetActive(show);

        buttonGroup.alpha = canPause ? 1f : 0.4f;
        buttonGroup.interactable = canPause;

        // Android Back pauses; while paused it's UI/Cancel, which the pause menu's back handler uses to resume
        var keyboard = Keyboard.current;
        if (canPause && TouchInput.IsMobile && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            PauseMenuController.Instance?.TogglePause();
    }

    private void OnPressed()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.CurrentGameState == GameManager.GameState.Playing)
            PauseMenuController.Instance?.TogglePause();
    }
}
