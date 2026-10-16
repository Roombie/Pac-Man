using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mobile-only back button (top-left), shown on any menu that can be backed out of
/// It does exactly what Cancel does there (BackButtonHandler), or steps back through character select
/// Drawn with the game's own arrow sprite so it looks like the rest of the menus
/// </summary>
[RequireComponent(typeof(MobileOverlayCanvas))]
public class MobileBackButton : MonoBehaviour
{
    private const string ArrowSpriteName = "ui_arrow_1"; // points right; the menus mirror it for "left"
    private static readonly Color ArcadeWhite = new(0.87f, 0.87f, 1f); // the menus' text colour

    private GameObject buttonObject;

    public static void Create()
    {
        if (FindAnyObjectByType<MobileBackButton>() != null) return;
        new GameObject("Mobile Back Button").AddComponent<MobileBackButton>();
    }

    private void Awake()
    {
        var button = GetComponent<MobileOverlayCanvas>().CreateCornerButton("Back", 0f);
        button.onClick.AddListener(OnPressed);
        buttonObject = button.gameObject;

        var arrow = new GameObject("Arrow", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        arrow.SetParent(buttonObject.transform, false);
        arrow.anchorMin = new Vector2(0.25f, 0.25f);
        arrow.anchorMax = new Vector2(0.75f, 0.75f);
        arrow.offsetMin = arrow.offsetMax = Vector2.zero;
        arrow.localScale = new Vector3(-1f, 1f, 1f); // mirrored to point left

        var image = arrow.GetComponent<Image>();
        image.sprite = FindSceneSprite(ArrowSpriteName);
        image.preserveAspect = true;
        image.color = ArcadeWhite;
        image.raycastTarget = false;

        buttonObject.SetActive(false);
    }

    private void Update()
    {
        bool show = TryGetBackTarget(out _, out _);
        if (buttonObject.activeSelf != show) buttonObject.SetActive(show);
    }

    private void OnPressed()
    {
        if (!TryGetBackTarget(out var handler, out var characterSelect)) return;

        if (handler != null) handler.GoBack();
        else characterSelect.TouchBack();
    }

    /// <summary>
    /// What "back" means on the menu currently open: its BackButtonHandler, or character select's own steps
    /// </summary>
    private static bool TryGetBackTarget(out BackButtonHandler handler, out CharacterSelectionManager characterSelect)
    {
        handler = null;
        characterSelect = null;

        var menu = MenuManager.Instance != null ? MenuManager.Instance.CurrentMenu : null;
        if (menu == null || !menu.activeInHierarchy) return false;

        handler = menu.GetComponent<BackButtonHandler>();
        if (handler != null && handler.isActiveAndEnabled) return true;

        handler = null;
        characterSelect = menu.GetComponentInChildren<CharacterSelectionManager>();
        return characterSelect != null;
    }

    private static Sprite FindSceneSprite(string spriteName)
    {
        foreach (var image in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (image.sprite != null && image.sprite.name == spriteName) return image.sprite;

        Debug.LogWarning($"[MobileBackButton] Sprite '{spriteName}' not found in the scene; the back button has no arrow");
        return null;
    }
}
