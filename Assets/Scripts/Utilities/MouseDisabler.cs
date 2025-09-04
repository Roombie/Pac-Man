using UnityEngine;
using UnityEngine.InputSystem;

public class MouseDisabler : MonoBehaviour
{
    void Awake()
    {
        if (Mouse.current != null)
        {
            // Turn off mouse device completely
            InputSystem.DisableDevice(Mouse.current);
        }
        
        Cursor.visible = false; // hide cursor
        Cursor.lockState = CursorLockMode.Locked; // lock cursor
    }
}