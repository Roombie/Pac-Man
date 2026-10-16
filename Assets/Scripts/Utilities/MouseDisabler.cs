using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

public class MouseDisabler : MonoBehaviour
{
    void Awake()
    {
        // Phones have no cursor, and touch testing in the Editor (Device Simulator,
        // "Simulate Touch Input From Mouse") is driven by the mouse, so leave it alone there
        if (TouchInput.IsMobile || TouchSimulation.instance != null)
            return;

        if (Mouse.current != null)
        {
            // Turn off mouse device completely
            InputSystem.DisableDevice(Mouse.current);
        }

        Cursor.visible = false; // hide cursor
        Cursor.lockState = CursorLockMode.Locked; // lock cursor
    }
}
