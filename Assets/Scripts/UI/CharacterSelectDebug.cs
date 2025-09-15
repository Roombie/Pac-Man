using UnityEngine;
using UnityEngine.InputSystem;
using System.Text;

public class CharacterSelectorDebug : MonoBehaviour
{
    [SerializeField] private CharacterSelectorPanel panel;

    private void OnGUI()
    {
        if (panel == null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"[Panel {panel.PlayerIndex + 1}]");
        sb.AppendLine($"Has Claimed Inputs: {panel.HasSelectedCharacter}");
        sb.AppendLine($"Chosen Scheme: {panel.GetInputSignature().scheme ?? "null"}");
        sb.AppendLine($"Reserved Scheme: {GetReservedScheme()}");
        sb.AppendLine($"Device IDs: {string.Join(",", panel.GetInputSignature().deviceIds)}");

        var playerInput = panel.GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            sb.AppendLine($"Current Control Scheme: {playerInput.currentControlScheme ?? "null"}");
            sb.AppendLine($"Binding Mask: {playerInput.actions?.bindingMask?.ToString() ?? "none"}");
        }

        GUI.Label(new Rect(10, 10 + (panel.PlayerIndex * 120), 500, 120), sb.ToString());
    }

    private string GetReservedScheme()
    {
        // Use reflection or a public property in CharacterSelectorPanel if available
        var field = typeof(CharacterSelectorPanel)
            .GetField("reservedKeyboardScheme", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return field?.GetValue(panel) as string ?? "null";
    }
}