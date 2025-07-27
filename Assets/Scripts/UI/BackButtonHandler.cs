using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class BackButtonHandler : MonoBehaviour
{
    public Button backButton;
    public AudioClip pressSound;
    public InputActionReference cancelAction;

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
        if (backButton != null && backButton.gameObject.activeInHierarchy)
        {
            AudioManager.Instance?.Play(pressSound, SoundCategory.SFX);
            backButton.onClick.Invoke();
        }
    }
}