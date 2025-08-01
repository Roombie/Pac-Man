using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.InputSystem.Users;
using TMPro;

public class InstructionTextBuilder : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text instructionText;

    [Header("Input Actions")]
    public InputActionReference submitAction;
    public InputActionReference cancelAction;

    [Header("Localized Strings")]
    public LocalizedString selectLine;   // "<sprite name=\"bulletList\">Use ← → to change character"
    public LocalizedString confirmLine;  // "<sprite name=\"bulletList\">Press {0} to confirm"
    public LocalizedString cancelLine;   // "<sprite name=\"bulletList\">Press {0} to cancel"

    private InputDevice lastDevice;

    private void OnEnable()
    {
        InputUser.onChange += OnUserChange;
        LocalizationSettings.SelectedLocaleChanged += _ => UpdateText();
        UpdateText();
    }

    private void OnDisable()
    {
        InputUser.onChange -= OnUserChange;
    }

    private void OnUserChange(InputUser user, InputUserChange change, InputDevice device)
    {
        if (change == InputUserChange.ControlSchemeChanged || change == InputUserChange.DeviceLost || change == InputUserChange.DeviceRegained)
        {
            lastDevice = device;
            UpdateText();
        }
    }

    private void UpdateText()
    {
        string submitKey = GetControlDisplayName(submitAction);
        string cancelKey = GetControlDisplayName(cancelAction);

        var select = selectLine.GetLocalizedStringAsync().WaitForCompletion();
        var confirm = confirmLine.GetLocalizedStringAsync(submitKey).WaitForCompletion();
        var cancel = cancelLine.GetLocalizedStringAsync(cancelKey).WaitForCompletion();

        instructionText.text = $"{select}\n{confirm}\n{cancel}";
    }

    private string GetControlDisplayName(InputActionReference actionRef)
    {
        if (actionRef == null || actionRef.action == null || actionRef.action.bindings.Count == 0)
            return "?";

        var action = actionRef.action;
        string layout = lastDevice?.layout ?? "";

        foreach (var binding in action.bindings)
        {
            if (!string.IsNullOrEmpty(binding.groups) && binding.groups.Contains(layout))
            {
                return InputControlPath.ToHumanReadableString(
                    binding.effectivePath,
                    InputControlPath.HumanReadableStringOptions.OmitDevice
                ).ToUpper();
            }
        }

        // Here it goes to the first binding
        return InputControlPath.ToHumanReadableString(
            action.bindings[0].effectivePath,
            InputControlPath.HumanReadableStringOptions.OmitDevice
        ).ToUpper();
    }
}