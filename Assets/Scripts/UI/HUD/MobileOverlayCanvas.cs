using UnityEngine;
using UnityEngine.UI;
// Device versions report the simulated phone in the Device Simulator
using Screen = UnityEngine.Device.Screen;

/// <summary>
/// Overlay canvas for mobile-only on-screen buttons (pause, back), drawn above the game's own UI
/// Its SafeArea child keeps buttons clear of notches and rounded corners
/// </summary>
public class MobileOverlayCanvas : MonoBehaviour
{
    public const float ButtonSize = 110f;  // reference units (1080x1920 canvas)
    public const float EdgeMargin = 24f;

    private static readonly Color ButtonBackground = new(0f, 0f, 0f, 0.35f);

    public RectTransform SafeArea { get; private set; }
    private Rect appliedSafeArea;

    private void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // above the game's canvases, below the 2P victory screen

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        SafeArea = new GameObject("Safe Area", typeof(RectTransform)).GetComponent<RectTransform>();
        SafeArea.SetParent(transform, false);
        ApplySafeArea();
    }

    private void Update()
    {
        if (Screen.safeArea != appliedSafeArea) ApplySafeArea();
    }

    /// <summary>
    /// A square button in a top corner of the safe area (x = 0 left, 1 right), with the game's dark backing
    /// </summary>
    public Button CreateCornerButton(string name, float cornerX)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(SafeArea, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(cornerX, 1f);
        rect.anchoredPosition = new Vector2(cornerX > 0.5f ? -EdgeMargin : EdgeMargin, -EdgeMargin);
        rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

        go.GetComponent<Image>().color = ButtonBackground;

        var button = go.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None }; // never steals keyboard/gamepad focus
        return button;
    }

    private void ApplySafeArea()
    {
        appliedSafeArea = Screen.safeArea;
        if (Screen.width == 0 || Screen.height == 0) return;

        SafeArea.anchorMin = new Vector2(appliedSafeArea.xMin / Screen.width, appliedSafeArea.yMin / Screen.height);
        SafeArea.anchorMax = new Vector2(appliedSafeArea.xMax / Screen.width, appliedSafeArea.yMax / Screen.height);
        SafeArea.offsetMin = SafeArea.offsetMax = Vector2.zero;
    }
}
