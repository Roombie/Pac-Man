using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class GameModeSelector : MonoBehaviour
{
    [Header("References")]
    public GameObject onePlayerModeObject;
    public GameObject twoPlayerModeObject;

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

        if (pauseAction != null)
        {
            pause = pauseAction.action;
            if (!pause.enabled) pause.Enable();
            pause.performed -= OnPausePressed;
            pause.performed += OnPausePressed;
        }

        if (submitAction != null)
        {
            submit = submitAction.action;
            if (!submit.enabled) submit.Enable();
            submit.performed -= OnSubmitPerformed;
            submit.performed += OnSubmitPerformed;
        }
    }

    private void OnDisable()
    {
        if (pause != null)
            pause.performed -= OnPausePressed;

        if (submit != null)
            submit.performed -= OnSubmitPerformed;
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
        PlayerPrefs.SetInt(SettingsKeys.PlayerCountKey, currentMode);
        PlayerPrefs.Save();
        ApplyMode();
    }

    private void ApplyMode()
    {
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        onePlayerModeObject?.SetActive(currentMode == 1);
        twoPlayerModeObject?.SetActive(currentMode == 2);
        Debug.Log($"Game Mode: {(currentMode == 1 ? "1P" : "2P")}");
    }
}