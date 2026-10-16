using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Touchscreen helpers for screens that poll input instead of using uGUI buttons
/// </summary>
public static class TouchInput
{
    private static readonly List<RaycastResult> uiHits = new();

    /// <summary>
    /// True on phones/tablets, including the Device Simulator in the Editor.
    /// Mobile-only UI (back button, tap hints, touch arrows) checks this
    /// </summary>
    public static bool IsMobile => UnityEngine.Device.Application.isMobilePlatform;

    /// <summary>
    /// True on the frame a short tap ends, unless it landed on a button (which handles it itself)
    /// </summary>
    public static bool TappedThisFrame()
    {
        var touch = Touchscreen.current?.primaryTouch;
        if (touch == null || !touch.tap.wasPressedThisFrame) return false;
        return !IsOverSelectable(touch.position.ReadValue());
    }

    /// <summary>
    /// True if a uGUI control (Button, Toggle, ...) is under the screen position
    /// HUD text and backgrounds can also be raycast targets, so only interactive controls count
    /// </summary>
    public static bool IsOverSelectable(Vector2 screenPosition)
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        uiHits.Clear();
        eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screenPosition }, uiHits);

        foreach (var hit in uiHits)
            if (hit.gameObject.GetComponentInParent<Selectable>() != null) return true;
        return false;
    }
}
