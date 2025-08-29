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

        if (OptionSelectorSettingHandler.TryCancelCurrentSelection())
            return;
            
        onCancelEvent?.Invoke();
    }
}