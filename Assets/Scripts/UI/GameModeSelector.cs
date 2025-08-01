using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class GameModeSelector : MonoBehaviour
{
    [Header("References")]
    public GameObject onePlayerModeObject;
    public GameObject twoPlayerModeObject;
    public AudioClip pressSound;

    [Header("Input")]
    public InputActionReference pauseAction;
    public InputActionReference submitAction;

    [Header("Events")]
    public UnityEvent onSubmitEvent;

    private InputAction pause; // cached action
    private InputAction submit;
    private int currentMode = 1; // 1 = 1P, 2 = 2P

    private void OnEnable()
    {
        // Load and apply saved mode
        currentMode = PlayerPrefs.GetInt(SettingsKeys.GameModeKey, 1);
        ApplyMode();

        // Cache and enable input
        if (pauseAction != null)
        {
            pause = pauseAction.action;
            pause.performed += OnPausePressed;
            pause.Enable();
        }

        if (submitAction != null)
        {
            submit = submitAction.action;
            submit.performed += OnSubmitPerformed;
            submit.Enable();
        }
    }

    private void OnDisable()
    {
        if (pause != null)
        {
            pause.performed -= OnPausePressed;
            pause.Disable();
        }

        if (submit != null)
        {
            submit.performed -= OnSubmitPerformed;
            submit.Disable();
        }
    }

    private void OnPausePressed(InputAction.CallbackContext ctx)
    {
        ToggleMode();
    }

    private void OnSubmitPerformed(InputAction.CallbackContext ctx)
    {
        onSubmitEvent?.Invoke();
    }

    private void ToggleMode()
    {
        currentMode = currentMode == 1 ? 2 : 1;
        PlayerPrefs.SetInt(SettingsKeys.GameModeKey, currentMode);
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