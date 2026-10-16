using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Turns touchscreen swipes into 4-way directions, polled once per frame
/// A direction fires as soon as the finger travels far enough, without waiting for release,
/// and the origin then moves to the finger so one touch can chain several turns
/// </summary>
public class SwipeDetector
{
    private const float MinSwipeInches = 0.12f;  // Travel needed before a swipe counts
    private const float FallbackDpi = 160f;      // Screen.dpi is 0 on some devices

    private bool tracking;
    private Vector2 origin;
    private Vector2 lastDirection;

    /// <summary>
    /// Returns true on the frame a swipe direction is recognized
    /// </summary>
    public bool TryGetSwipe(out Vector2 direction)
    {
        direction = Vector2.zero;

        var touch = Touchscreen.current?.primaryTouch;
        if (touch == null) return false;

        Vector2 position = touch.position.ReadValue();

        if (touch.press.wasPressedThisFrame)
        {
            // Touches that start on a button (pause, menus) are not swipes
            tracking = !TouchInput.IsOverSelectable(position);
            origin = position;
            lastDirection = Vector2.zero;
            return false;
        }

        if (!touch.press.isPressed)
        {
            tracking = false;
            return false;
        }

        if (!tracking) return false;

        Vector2 delta = position - origin;
        float dpi = Screen.dpi > 0f ? Screen.dpi : FallbackDpi;
        if (delta.magnitude < MinSwipeInches * dpi) return false;

        direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
            ? new Vector2(Mathf.Sign(delta.x), 0f)
            : new Vector2(0f, Mathf.Sign(delta.y));

        origin = position;

        // Keeping the finger moving the same way shouldn't re-fire every few pixels
        if (direction == lastDirection) return false;
        lastDirection = direction;
        return true;
    }
}
