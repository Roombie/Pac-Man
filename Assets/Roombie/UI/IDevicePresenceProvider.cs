using System;

public interface IDevicePresenceProvider
{
    int ExpectedPlayers { get; }
    /// <summary>Return a scheme/layout string like "Gamepad", "Keyboard", "P1Keyboard". Empty/null == none.</summary>
    string GetSchemeForSlot(int slotIndex);
    event Action<int, string> OnSlotSchemeChanged;
}