using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class BackButtonHandler : MonoBehaviour
{
    public AudioClip pressSound;
    public InputActionReference cancelAction;
    public UnityEvent onCancelEvent;

    private InputAction cancel;

    private void OnEnable()
    {
        if (cancelAction != null)
        {
            cancel = cancelAction.action;
            cancel.performed += OnCancelPressed;
            cancel.Enable();
        }
    }

    private void OnDisable()
    {
        if (cancel != null)
        {
            cancel.performed -= OnCancelPressed;
            cancel.Disable();
        }
    }

    private void OnCancelPressed(InputAction.CallbackContext ctx)
    {
        onCancelEvent?.Invoke();
        AudioManager.Instance?.Play(pressSound, SoundCategory.SFX);
        ArrowSelector.Instance?.SuppressSoundOnNextSelection();
        MenuManager.Instance.Back();
    }
}