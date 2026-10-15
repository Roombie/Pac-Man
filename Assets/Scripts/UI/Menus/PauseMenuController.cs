using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Owns the full pause lifecycle:
///   - Toggling pause on/off (timeScale, input map, game state)
///   - Showing/hiding the pause menu panel
///   - Handling all navigation input while paused
///
/// Other scripts call PauseMenuController.Instance.TogglePause() / Pause() / Resume().
/// GameManager no longer holds any pause logic.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    public static PauseMenuController Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject pauseMenu;

    [Header("Navigation Timing")]
    [SerializeField] private float repeatDelay    = 0.4f;
    [SerializeField] private float repeatInterval = 0.1f;

    private bool isPaused = false;
    private float nextMoveTime = 0f;
    private bool holdingDirection = false;
    private Vector2 lastDirection   = Vector2.zero;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!isPaused) return;
        HandleNavigationInput();
    }

    public void TogglePause()
    {
        if (InputManager.Instance.waitingForRejoin) return;

        if (GameManager.Instance.CurrentGameState == GameManager.GameState.Playing)
            Pause();
        else if (GameManager.Instance.CurrentGameState == GameManager.GameState.Paused)
        {
            AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
            Resume();
        }
    }

    public void Pause()
    {
        Time.timeScale = 0f;

        ShowMenu();

        // Block gameplay input while paused; menu navigation is polled below
        InputManager.Instance.SetGameplayInputEnabled(false);

        GameManager.Instance.SetState(GameManager.GameState.Paused);
        Debug.Log("[PauseMenuController] Game paused");
    }

    public void Resume()
    {
        Time.timeScale = 1f;

        HideMenu();

        // Restore gameplay input
        InputManager.Instance.SetGameplayInputEnabled(true);

        GameManager.Instance.SetState(GameManager.GameState.Playing);
        Debug.Log("[PauseMenuController] Game resumed");
    }

    /// <summary>
    /// Shows the menu panel without changing game state or timeScale.
    /// Used by InputManager.RequestDejoin which manages its own state.
    /// </summary>
    public void ShowMenu()
    {
        if (pauseMenu != null) MenuManager.Instance.OpenMenu(pauseMenu);
        isPaused        = true;
        holdingDirection = false;
        lastDirection   = Vector2.zero;
    }

    /// <summary>
    /// Hides the menu panel without changing game state or timeScale.
    /// </summary>
    public void HideMenu()
    {
        MenuManager.Instance.CloseAll();
        isPaused = false;
    }

    private void HandleNavigationInput()
    {
        var kb = Keyboard.current;
        var gp = Gamepad.current;

        // Submit
        bool submitPressed = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                        || (gp != null && gp.buttonSouth.wasPressedThisFrame);

        if (submitPressed)
        {
            if (!OptionSelectorSettingHandler.IsCurrentlySelecting)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected != null)
                {
                    // Check for a standard Button first
                    var button = selected.GetComponent<Button>();
                    if (button != null)
                    {
                        button.onClick.Invoke();
                    }
                    // If no button, check if it's your ToggleSettingHandler
                    else
                    {
                        var toggleHandler = selected.GetComponent<ToggleSettingHandler>();
                        if (toggleHandler != null)
                        {
                            toggleHandler.Toggle();
                        }
                    }
                }
            }
            return;
        }

        // --- OptionSelector mid-edit logic (Forward Left/Right) ---
        if (OptionSelectorSettingHandler.IsCurrentlySelecting)
        {
            Vector2 lr = Vector2.zero;
            if (kb != null)
            {
                if      (kb.leftArrowKey.isPressed  || kb.aKey.isPressed) lr = Vector2.left;
                else if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) lr = Vector2.right;
            }
            if (lr == Vector2.zero && gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if      (gp.dpad.left.isPressed  || stick.x < -0.5f) lr = Vector2.left;
                else if (gp.dpad.right.isPressed || stick.x >  0.5f) lr = Vector2.right;
            }

            if (lr != Vector2.zero)
            {
                float now = Time.unscaledTime;
                if (!holdingDirection || lr != lastDirection)
                {
                    holdingDirection = true;
                    lastDirection    = lr;
                    nextMoveTime     = now + repeatDelay;
                    OptionSelectorSettingHandler.NavigateCurrent(lr.x > 0 ? 1 : -1);
                }
                else if (now >= nextMoveTime)
                {
                    nextMoveTime = now + repeatInterval;
                    OptionSelectorSettingHandler.NavigateCurrent(lr.x > 0 ? 1 : -1);
                }
            }
            else
            {
                holdingDirection = false;
                lastDirection    = Vector2.zero;
            }
            return;
        }

        // --- Normal Up/Down navigation ---
        Vector2 nav = Vector2.zero;
        if (kb != null)
        {
            if      (kb.upArrowKey.isPressed   || kb.wKey.isPressed) nav = Vector2.up;
            else if (kb.downArrowKey.isPressed || kb.sKey.isPressed) nav = Vector2.down;
        }
        if (nav == Vector2.zero && gp != null)
        {
            Vector2 stick = gp.leftStick.ReadValue();
            if      (gp.dpad.up.isPressed   || stick.y >  0.5f) nav = Vector2.up;
            else if (gp.dpad.down.isPressed || stick.y < -0.5f) nav = Vector2.down;
        }

        if (nav == Vector2.zero)
        {
            holdingDirection = false;
            lastDirection    = Vector2.zero;
            return;
        }

        float t = Time.unscaledTime;
        if (!holdingDirection || nav != lastDirection)
        {
            holdingDirection = true;
            lastDirection    = nav;
            nextMoveTime     = t + repeatDelay;
            Navigate(nav);
        }
        else if (t >= nextMoveTime)
        {
            nextMoveTime = t + repeatInterval;
            Navigate(nav);
        }
    }

    private void Navigate(Vector2 direction)
    {
        var current = EventSystem.current.currentSelectedGameObject;
        if (current == null) return;

        var selectable = current.GetComponent<Selectable>();
        if (selectable == null) return;

        Selectable next = direction.y > 0
            ? selectable.FindSelectableOnUp()
            : selectable.FindSelectableOnDown();

        if (next != null)
            EventSystem.current.SetSelectedGameObject(next.gameObject);
    }
}