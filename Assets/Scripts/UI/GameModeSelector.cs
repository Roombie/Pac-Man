using UnityEngine;
using UnityEngine.InputSystem;

public class GameModeSelector : MonoBehaviour
{
    [Header("References")]
    public GameObject onePlayerModeObject;
    public GameObject twoPlayerModeObject;
    public AudioClip pressSound;

    [Header("Input")]
    public InputActionReference pauseAction;

    private InputAction pause; // cached action
    private int currentMode = 1; // 1 = 1P, 2 = 2P

    private void OnEnable()
    {
        // Load and apply saved mode
        currentMode = PlayerPrefs.GetInt("GameMode", 1);
        ApplyMode();

        // Cache and enable input
        if (pauseAction != null)
        {
            pause = pauseAction.action;
            pause.performed += OnPausePressed;
            pause.Enable();
        }
    }

    private void OnDisable()
    {
        if (pause != null)
        {
            pause.performed -= OnPausePressed;
            pause.Disable();
        }
    }

    private void OnPausePressed(InputAction.CallbackContext ctx)
    {
        ToggleMode();
    }

    private void ToggleMode()
    {
        currentMode = currentMode == 1 ? 2 : 1;
        PlayerPrefs.SetInt("GameMode", currentMode);
        PlayerPrefs.Save();
        ApplyMode();
    }

    private void ApplyMode()
    {
        AudioManager.Instance.Play(pressSound, SoundCategory.SFX);
        onePlayerModeObject?.SetActive(currentMode == 1);
        twoPlayerModeObject?.SetActive(currentMode == 2);
        Debug.Log($"Game Mode: {(currentMode == 1 ? "1P" : "2P")}");
    }
}
