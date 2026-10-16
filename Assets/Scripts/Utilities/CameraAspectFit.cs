using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
// Device versions report the simulated phone in the Device Simulator
using Screen = UnityEngine.Device.Screen;

/// <summary>
/// Makes the maze and its UI fit any screen shape, from wide phones in landscape to tall ones in portrait
///   - Camera: keeps its scene size whenever that shows enough width, and only zooms out on narrow screens
///   - UI: the canvases drawn by this camera scale with the camera's view instead of the screen width,
///     so the HUD keeps the same place around the maze on every aspect ratio (it was laid out on 16:9;
///     on wider screens the score and lives rows used to fall off the top and bottom)
///   - Portrait: side panels that no longer fit next to the maze become a strip above the score row
///     (see PortraitPanel)
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraAspectFit : MonoBehaviour
{
    // Scenes whose world content is laid out around the maze (34 tiles wide)
    private static readonly string[] FittedScenes = { "MainMenu", "Pacman" };
    private const float MazeWidthWithMargin = 36f;

    // The aspect the UI was designed on: its canvas height then is referenceWidth / DesignAspect
    private const float DesignAspect = 16f / 9f;

    // Side panels that sit left/right of the maze in landscape and become a strip above the HUD in portrait
    private static readonly string[] PortraitTopPanels = { "Info Round Rectangle Image" };
    private const float PortraitPanelGap = 10f;   // canvas units between the score row and the moved panel
    private const float PortraitTopMargin = 30f;  // canvas units kept free above it (notches, status bar)

    [Tooltip("World units that must always fit horizontally")]
    public float minVisibleWidth = MazeWidthWithMargin;

    private struct FittedCanvas
    {
        public RectTransform rect;
        public CanvasScaler scaler;
        public float designHeight; // canvas units covering the camera's original view height
    }

    private struct MovedPanel
    {
        public PortraitPanel layout;
        public FittedCanvas canvas;
    }

    private Camera cam;
    private float baseSize;
    private int lastWidth, lastHeight;
    private readonly List<FittedCanvas> canvases = new();
    private readonly List<MovedPanel> panels = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSceneLoads() => SceneManager.sceneLoaded += OnSceneLoaded;

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (System.Array.IndexOf(FittedScenes, scene.name) < 0) return;

        var main = Camera.main;
        if (main != null && main.orthographic && main.GetComponent<CameraAspectFit>() == null)
            main.gameObject.AddComponent<CameraAspectFit>();
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        baseSize = cam.orthographicSize;
        CollectCanvases();
        Fit();
    }

    private void LateUpdate()
    {
        if (Screen.width != lastWidth || Screen.height != lastHeight) Fit();
    }

    private void CollectCanvases()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceCamera) continue;
            if (canvas.worldCamera != cam || canvas.gameObject.scene != gameObject.scene) continue;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;

            var fitted = new FittedCanvas
            {
                rect = (RectTransform)canvas.transform,
                scaler = scaler,
                designHeight = scaler.referenceResolution.x / DesignAspect,
            };
            canvases.Add(fitted);

            foreach (var rect in canvas.GetComponentsInChildren<RectTransform>(true))
                if (System.Array.IndexOf(PortraitTopPanels, rect.name) >= 0)
                    panels.Add(new MovedPanel { layout = new PortraitPanel(rect, fitted.rect), canvas = fitted });
        }
    }

    private void Fit()
    {
        lastWidth = Screen.width;
        lastHeight = Screen.height;
        if (Screen.height == 0) return;

        float aspect = (float)Screen.width / Screen.height;
        bool portrait = aspect < 1f;

        float size = Mathf.Max(baseSize, minVisibleWidth / 2f / aspect);
        if (portrait) size = Mathf.Max(size, PortraitSizeForPanels());
        cam.orthographicSize = size;
        float zoomOut = size / baseSize;

        // Height-matched scaling with a height that grows with the zoom keeps canvas units locked to world units
        foreach (var c in canvases)
        {
            if (c.scaler == null) continue;
            c.scaler.matchWidthOrHeight = 1f;
            c.scaler.referenceResolution = new Vector2(c.scaler.referenceResolution.x, c.designHeight * zoomOut);
        }

        foreach (var p in panels)
        {
            if (!p.layout.IsValid) continue;
            if (portrait) p.layout.ApplyPortrait(p.canvas.designHeight / 2f + PortraitPanelGap);
            else p.layout.ApplyLandscape();
        }
    }

    /// <summary>
    /// Camera size that leaves room above the score row for the tallest moved panel
    /// Tall phones have room to spare; squarer screens (tablets) zoom out a little more for it
    /// </summary>
    private float PortraitSizeForPanels()
    {
        float needed = 0f; // ratio of canvas height needed vs the original view's
        foreach (var p in panels)
        {
            if (!p.layout.IsValid) continue;
            float viewHalf = p.canvas.designHeight / 2f;
            needed = Mathf.Max(needed, (viewHalf + PortraitPanelGap + p.layout.PortraitHeight + PortraitTopMargin) / viewHalf);
        }
        return baseSize * needed;
    }
}
