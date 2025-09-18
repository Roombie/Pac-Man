using UnityEngine;
using UnityEngine.InputSystem;
using System.Text;

/// <summary>
/// Debug overlay for one character selection panel.
/// Displays panel state and input info on screen.
/// </summary>
public class CharacterSelectDebug : MonoBehaviour
{
    [SerializeField] private PanelState panelState;
    [SerializeField] private PanelInputHandler inputHandler;

    private void OnGUI()
    {
        if (panelState == null || inputHandler == null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"[Panel {panelState.PlayerIndex + 1}]");

        // State info
        sb.AppendLine($"Has Selected Character: {panelState.HasSelectedCharacter}");
        sb.AppendLine($"Has Confirmed Skin: {panelState.HasConfirmedSkin}");
        sb.AppendLine($"Has Confirmed Final: {panelState.HasConfirmedFinal}");

        // Input info
        var sig = inputHandler.GetInputSignature();
        sb.AppendLine($"Chosen Scheme: {sig.scheme ?? "null"}");
        sb.AppendLine($"Device IDs: {(sig.deviceIds != null ? string.Join(",", sig.deviceIds) : "none")}");

        // PlayerInput internals
        var playerInput = inputHandler.GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            sb.AppendLine($"Current Control Scheme: {playerInput.currentControlScheme ?? "null"}");
            sb.AppendLine($"Binding Mask: {playerInput.actions?.bindingMask?.ToString() ?? "none"}");
        }

        // Draw the block
        GUI.Label(new Rect(10, 10 + (panelState.PlayerIndex * 150), 500, 150), sb.ToString());
    }
}