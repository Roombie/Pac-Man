using UnityEngine;
using UnityEngine.InputSystem;

namespace Roombie.CharacterSelect
{
    /// <summary>
    /// Watches Input System device changes and auto-dejoins any panel that loses its reserved gamepad.
    /// Safe to keep alive across scenes (Do not destroy) if you want.
    /// </summary>
    public class DeviceHotplugWatcher : MonoBehaviour
    {
        [Header("Behavior")]
        [Tooltip("If a reserved gamepad disconnects or is removed, the owning panel will be force-dejoined.")]
        public bool dejoinOnGamepadDisconnect = true;

        [Tooltip("Also dejoin when the device is Disabled (platform/driver toggles).")]
        public bool dejoinOnGamepadDisabled = true;

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void OnDeviceChange(InputDevice dev, InputDeviceChange change)
        {
            if (dev is not Gamepad gp) return;

            bool shouldDejoin =
                (dejoinOnGamepadDisconnect && (change == InputDeviceChange.Disconnected || change == InputDeviceChange.Removed)) ||
                (dejoinOnGamepadDisabled   && change == InputDeviceChange.Disabled);

            if (!shouldDejoin) return;

            var sel = CharacterSelectionManager.Instance;
            if (sel == null) return;

            // include inactive for safety, no sorting needed (faster).
            var handlers = FindObjectsByType<PanelInputHandler>(
                FindObjectsInactive.Include, FindObjectsSortMode.None
            );

            foreach (var h in handlers)
            {
                if (h != null && h.IsUsingGamepad(gp))
                {
                    Debug.Log($"[DeviceHotplugWatcher] Gamepad {gp.deviceId} disconnected → force dejoin P{h.PlayerIndex + 1}");
                    h.ForceDejoin();
                }
            }
        }
    }
}