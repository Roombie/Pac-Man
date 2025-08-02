using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using TMPro;
using System.Collections.Generic;

public class InstructionTextBuilder : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text instructionText;

    [Header("Input Actions")]
    public InputActionReference leftAction;
    public InputActionReference rightAction;
    public InputActionReference submitAction;
    public InputActionReference cancelAction;

    [Header("Localized Strings")]
    public LocalizedString selectLine;
    public LocalizedString confirmLine;
    public LocalizedString cancelLine;

    private enum InputType { None, Keyboard, Gamepad }

    private InputType currentInputType = InputType.None;
    private InputDevice lastDevice = null;
    private string lastDeviceKey = "";

    private void OnEnable()
    {
        DetectInitialInputDevice();
        RegisterInputCallbacks(true);
        LocalizationSettings.SelectedLocaleChanged += _ => UpdateText(forceUpdate: true);
        UpdateText(forceUpdate: true);
    }

    private void DetectInitialInputDevice()
    {
        if (Gamepad.current != null)
        {
            currentInputType = InputType.Gamepad;
            lastDevice = Gamepad.current;
        }
        else if (Keyboard.current != null)
        {
            currentInputType = InputType.Keyboard;
            lastDevice = Keyboard.current;
        }
    }

    private void OnDisable()
    {
        RegisterInputCallbacks(false);
        LocalizationSettings.SelectedLocaleChanged -= _ => UpdateText(forceUpdate: true);
    }

    private void RegisterInputCallbacks(bool subscribe)
    {
        void Toggle(InputActionReference actionRef, System.Action<InputAction.CallbackContext> callback)
        {
            if (actionRef?.action == null) return;

            if (subscribe)
                actionRef.action.performed += callback;
            else
                actionRef.action.performed -= callback;
        }

        Toggle(leftAction, OnActionPerformed);
        Toggle(rightAction, OnActionPerformed);
        Toggle(submitAction, OnActionPerformed);
        Toggle(cancelAction, OnActionPerformed);
    }

    private void OnActionPerformed(InputAction.CallbackContext ctx)
    {
        var device = ctx.control.device;

        InputType detectedType = device switch
        {
            Gamepad => InputType.Gamepad,
            Keyboard => InputType.Keyboard,
            _ => currentInputType
        };

        if (detectedType != currentInputType)
        {
            currentInputType = detectedType;
            lastDevice = device;
            UpdateText();
        }
    }

    private void UpdateText(bool forceUpdate = false)
    {
        string leftIcon = GetBindingDisplay(leftAction, "left");
        string rightIcon = GetBindingDisplay(rightAction, "right");
        string submitIcon = GetBindingDisplay(submitAction);
        string cancelIcon = GetBindingDisplay(cancelAction);

        string currentKey = $"{leftIcon}_{rightIcon}_{submitIcon}_{cancelIcon}";

        if (!forceUpdate && currentKey == lastDeviceKey)
            return;

        lastDeviceKey = currentKey;

        string combinedLR = $"{leftIcon} {rightIcon}";

        var select = selectLine.GetLocalizedStringAsync(combinedLR).WaitForCompletion();
        var confirm = confirmLine.GetLocalizedStringAsync(submitIcon).WaitForCompletion();
        var cancel = cancelLine.GetLocalizedStringAsync(cancelIcon).WaitForCompletion();

        instructionText.text = $"{select}\n{confirm}\n{cancel}";
    }

    private string GetBindingDisplay(InputActionReference actionRef, string compositePart = null)
    {
        if (actionRef?.action == null || actionRef.action.bindings.Count == 0)
            return "?";

        if (!string.IsNullOrEmpty(compositePart))
        {
            string spriteKey = (currentInputType, compositePart) switch
            {
                (InputType.Gamepad, "left") => "leftstickleft",
                (InputType.Gamepad, "right") => "leftstickright",
                (InputType.Keyboard, "left") => "leftarrow",
                (InputType.Keyboard, "right") => "rightarrow",
                _ => compositePart
            };

            return TryGetSpriteIcon(spriteKey) ?? GetFallbackText(spriteKey);
        }

        var action = actionRef.action;
        string controlPath = null;

        foreach (var binding in action.bindings)
        {
            if (lastDevice != null)
            {
                var foundControl = InputSystem.FindControl(binding.effectivePath);
                if (foundControl != null && foundControl.device == lastDevice)
                {
                    controlPath = binding.effectivePath;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(controlPath))
        {
            foreach (var binding in action.bindings)
            {
                controlPath = binding.effectivePath;
                break;
            }
        }

        if (string.IsNullOrEmpty(controlPath))
            return "?";

        string key = controlPath
            .Replace("<Gamepad>/", "")
            .Replace("<Keyboard>/", "")
            .Replace("<XInputController>/", "")
            .Replace("<DualShockGamepad>/", "")
            .Replace("<DualSenseGamepad>/", "")
            .Replace("<HID>/", "")
            .ToLower();

        string icon = TryGetSpriteIcon(key);

        if (string.IsNullOrEmpty(icon) && key.Contains("/"))
        {
            string fallback = key.Split('/')[0];
            icon = TryGetSpriteIcon(fallback);
        }

        if (string.IsNullOrEmpty(icon))
        {
            var resolvedControl = InputSystem.FindControl(controlPath);
            string resolvedKey = resolvedControl?.name?.ToLower() ?? "?";
            icon = TryGetSpriteIcon(resolvedKey);
        }

        return icon ?? GetFallbackText(key);
    }

    private string TryGetSpriteIcon(string key)
    {
        List<TMP_SpriteAsset> assetsToSearch = new();
        if (instructionText.spriteAsset != null)
            assetsToSearch.Add(instructionText.spriteAsset);

        if (instructionText.spriteAsset?.fallbackSpriteAssets != null)
            assetsToSearch.AddRange(instructionText.spriteAsset.fallbackSpriteAssets);

        foreach (var spriteAsset in assetsToSearch)
        {
            if (spriteAsset == null) continue;

            foreach (var sprite in spriteAsset.spriteCharacterTable)
            {
                if (sprite.name.Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    return $"<sprite name=\"{sprite.name}\">";
                }
            }
        }

        return null;
    }

    private string GetFallbackText(string key)
    {
        return key switch
        {
            "leftarrow" => "←",
            "rightarrow" => "→",
            "a" => "A",
            "d" => "D",
            "leftstickleft" => "L←",
            "leftstickright" => "L→",
            "dpadleft" => "D←",
            "dpadright" => "D→",
            "enter" or "return" => "ENTER",
            "space" => "SPACE",
            "esc" or "escape" => "ESC",
            "tab" => "TAB",
            "backspace" => "BACK",
            "leftctrl" or "rightctrl" => "CTRL",
            "leftshift" or "rightshift" => "SHIFT",
            "leftalt" or "rightalt" => "ALT",
            "delete" => "DEL",
            _ => key.ToUpper()
        };
    }
}