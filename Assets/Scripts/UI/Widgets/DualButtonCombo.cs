using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class DualButtonCombo : MonoBehaviour
{
    [SerializeField] private InputActionReference button1Action;
    [SerializeField] private InputActionReference button2Action;

    [Tooltip("Event triggered when both buttons are pressed simultaneously.")]
    public UnityEvent onComboActivated;

    private void Update()
    {
        if (button1Action.action.IsPressed() && button2Action.action.IsPressed())
        {
            onComboActivated?.Invoke();
        }
    }
}