using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class BackButtonHandler : MonoBehaviour
{
    public InputActionReference cancelAction;
    public UnityEvent onCancelEvent;

    private InputAction cancel;

    private void OnEnable()
    {
        if (cancelAction != null)
        {
            cancel = cancelAction.action;

            if (!cancel.enabled)
                cancel.Enable();

            cancel.performed -= OnCancelPressed;
            cancel.performed += OnCancelPressed;
        }
    }

    private void OnDisable()
    {
        if (cancel != null)
        {
            cancel.performed -= OnCancelPressed;
        }
    }

    private void OnCancelPressed(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        GoBack();
    }

    /// <summary>
    /// Same as pressing Cancel: first leaves an option being edited, otherwise backs out of this menu
    /// Used by the on-screen back button on mobile
    /// </summary>
    public void GoBack()
    {
        if (OptionSelectorSettingHandler.TryCancelCurrentSelection())
            return;

        onCancelEvent?.Invoke();
    }
}