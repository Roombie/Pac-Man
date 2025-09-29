using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;

namespace Roombie.UI
{
    /// Simple provider that reports which scheme last pinged for the active slot.
    /// It only cares about "Keyboard" vs "Gamepad".
    /// GameManager will usually drive the overlay; this is optional glue if you want it.
    public class GameplayDevicePresenceProvider : MonoBehaviour, IDevicePresenceProvider
    {
        [Tooltip("If > 0, forces this player count. If 0, uses GameManager (1 or 2).")]
        [SerializeField] int expectedPlayersOverride = 0;

        // simple storage per-slot
        string[] _schemes = Array.Empty<string>();

        public int ExpectedPlayers
        {
            get
            {
                if (expectedPlayersOverride > 0) return expectedPlayersOverride;

                var gm = GameManager.Instance;
                if (gm) return Mathf.Max(1, gm.PlayerCount);

                // Fallback (e.g., during early scene init)
                return Mathf.Max(1, PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 1));
            }
        }

        public event Action<int, string> OnSlotSchemeChanged;

        void OnEnable()
        {
            ResizeBuffer();
            // SAFE increment — never let this go negative
            InputUser.listenForUnpairedDeviceActivity = Mathf.Max(0, InputUser.listenForUnpairedDeviceActivity) + 1;
            InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsed;
        }

        void OnDisable()
        {
            // SAFE decrement — only if > 0
            var cur = InputUser.listenForUnpairedDeviceActivity;
            if (cur > 0) InputUser.listenForUnpairedDeviceActivity = cur - 1;

            InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsed;
        }

        void ResizeBuffer()
        {
            int n = Mathf.Max(1, ExpectedPlayers);
            if (_schemes.Length != n) Array.Resize(ref _schemes, n);
        }

        void OnUnpairedDeviceUsed(InputControl control, InputEventPtr _)
        {
            if (control == null) return;

            // which slot do we update? Use the active gameplay slot from GameManager if available
            int slot = 0;
            var gm = GameManager.Instance;
            if (gm) slot = Mathf.Clamp(gm.GetCurrentIndex(), 0, Mathf.Max(1, ExpectedPlayers) - 1);

            string scheme = null;
            if (control.device is Gamepad) scheme = "Gamepad";
            else if (control.device is Keyboard) scheme = "Keyboard";
            else return;

            ResizeBuffer();

            if (_schemes[slot] != scheme)
            {
                _schemes[slot] = scheme;
                OnSlotSchemeChanged?.Invoke(slot, scheme);
            }
        }

        public string GetSchemeForSlot(int slotIndex)
        {
            if (_schemes == null || _schemes.Length == 0) return null;
            if (slotIndex < 0 || slotIndex >= _schemes.Length) return null;
            return _schemes[slotIndex];
        }
    }
}
