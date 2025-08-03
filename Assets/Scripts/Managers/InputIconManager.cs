using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Localization;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEngine.Localization.Settings;

public class InputIconManager : MonoBehaviour
{
    [Header("Instructions")]
    public List<InstructionGroup> instructionGroups = new();

    [Header("Icon Images")]
    public List<ActionIcon> icons = new();

    [System.Serializable]
    public class InstructionGroup
    {
        public TMP_Text targetText;
        public LocalizedString instructionFormat;
        public List<InputActionReference> actions = new();
        public List<string> compositeParts = new();
    }

    [System.Serializable]
    public class ActionIcon
    {
        public string actionName;
        public Image targetImage;
        public Sprite keyboardSprite;
        public Sprite gamepadSprite;
    }

    private enum InputType { None, Keyboard, Gamepad }
    private InputType currentInputType = InputType.None;
    private InputDevice lastDevice = null;
    private readonly Dictionary<string, string> spriteNameCache = new();

    private void OnEnable()
    {
        DetectInitialInputDevice();
        InputUser.listenForUnpairedDeviceActivity = 1;
        InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsed;
        LocalizationSettings.SelectedLocaleChanged += _ => UpdateInstructionTexts();
        UpdateAll();
    }

    private void OnDisable()
    {
        InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsed;
        LocalizationSettings.SelectedLocaleChanged -= _ => UpdateInstructionTexts();
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

    private void OnUnpairedDeviceUsed(InputControl control, InputEventPtr eventPtr)
    {
        if (control?.device is Mouse) return;

        InputType detectedType = control.device switch
        {
            Gamepad => InputType.Gamepad,
            Keyboard => InputType.Keyboard,
            _ => currentInputType
        };

        if (detectedType != currentInputType)
        {
            currentInputType = detectedType;
            lastDevice = control.device;
            UpdateAll();
        }
    }

    private void UpdateAll()
    {
        UpdateIcons();
        UpdateInstructionTexts();
    }

    private void UpdateIcons()
    {
        foreach (var icon in icons)
        {
            if (icon.targetImage == null) continue;

            icon.targetImage.sprite = currentInputType switch
            {
                InputType.Gamepad => icon.gamepadSprite,
                InputType.Keyboard => icon.keyboardSprite,
                _ => icon.keyboardSprite
            };
        }
    }

    private async void UpdateInstructionTexts()
    {
        // help this part took so long
        // the worst part is that I forgot to clear the addressables groups cache
        // and turns out that what was causing one of the sprite icons to not show
        // Note: DON'T FORGET to do a new build every time you change something on the localization tables
        foreach (var group in instructionGroups)
        {
            if (group.targetText == null || group.instructionFormat.IsEmpty) continue;

            var expectedIndices = new List<int>();
            var rawFormat = group.instructionFormat.GetLocalizedString();
            var matches = Regex.Matches(rawFormat, "\\{(\\d+)\\}");
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out int index) && !expectedIndices.Contains(index))
                    expectedIndices.Add(index);
            }

            expectedIndices.Sort();
            List<string> resolvedIcons = new();
            int totalSlots = expectedIndices.Count;

            for (int i = 0; i < totalSlots; i++)
            {
                string part = i < group.compositeParts.Count ? group.compositeParts[i] : null;
                InputActionReference actionRef = group.actions.Count == 1 ? group.actions[0] : (i < group.actions.Count ? group.actions[i] : null);
                resolvedIcons.Add(GetBindingDisplay(actionRef, part));
            }

            try
            {
                var localized = await group.instructionFormat.GetLocalizedStringAsync(resolvedIcons.ToArray()).Task;
                group.targetText.text = localized;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[InputIconManager] Format error: {e.Message} | Format: '{rawFormat}' | Args: {resolvedIcons.Count}");
                group.targetText.text = rawFormat + " " + string.Join(" ", resolvedIcons);
            }
        }
    }
    
    private string GetBindingDisplay(InputActionReference actionRef, string compositePart = null)
    {
        if (actionRef?.action == null || actionRef.action.bindings.Count == 0) return "?";

        if (!string.IsNullOrEmpty(compositePart))
        {
            var compositeAction = actionRef.action;
            string partName = compositePart.ToLower();
            string bindingKey = null;

            foreach (var binding in compositeAction.bindings)
            {
                if (binding.isPartOfComposite && binding.name.ToLower() == partName)
                {
                    bindingKey = GetCleanBindingPath(binding.effectivePath);
                    break;
                }
            }

            string spriteKey = (currentInputType, partName) switch
            {
                (InputType.Gamepad, "left") => "leftstickleft",
                (InputType.Gamepad, "right") => "leftstickright",
                (InputType.Keyboard, "left") => "leftarrow",
                (InputType.Keyboard, "right") => "rightarrow",
                _ => bindingKey ?? partName
            };

            return TryGetSpriteIcon(spriteKey) ?? GetFallbackText(spriteKey);
        }
        ;

        var action = actionRef.action;
        string controlPath = null;

        if (lastDevice != null)
        {
            foreach (var binding in action.bindings)
            {
                var control = InputSystem.FindControl(binding.effectivePath);
                if (control != null && control.device == lastDevice)
                {
                    controlPath = binding.effectivePath;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(controlPath) && action.bindings.Count > 0)
            controlPath = action.bindings[0].effectivePath;

        if (string.IsNullOrEmpty(controlPath)) return "?";

        string key = GetCleanBindingPath(controlPath);
        string icon = TryGetSpriteIcon(key)
            ?? (key.Contains("/") ? TryGetSpriteIcon(key.Split('/')[0]) : null)
            ?? TryGetSpriteIcon(InputSystem.FindControl(controlPath)?.name?.ToLower() ?? "?");

        return icon ?? GetFallbackText(key);
    }

    private string GetCleanBindingPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        return Regex.Replace(path.ToLower(), "<[^>]+>/", "");
    }

    private string TryGetSpriteIcon(string key)
    {
        if (spriteNameCache.TryGetValue(key, out var cached)) return cached;

        List<TMP_SpriteAsset> assetsToSearch = new();

        foreach (var group in instructionGroups)
        {
            if (group.targetText?.spriteAsset != null)
            {
                assetsToSearch.Add(group.targetText.spriteAsset);
                if (group.targetText.spriteAsset.fallbackSpriteAssets != null)
                    assetsToSearch.AddRange(group.targetText.spriteAsset.fallbackSpriteAssets);
            }
        }

        foreach (var spriteAsset in assetsToSearch)
        {
            if (spriteAsset == null) continue;

            foreach (var sprite in spriteAsset.spriteCharacterTable)
            {
                if (sprite.name.Equals(key, System.StringComparison.OrdinalIgnoreCase))
                {
                    var result = $"<sprite name=\"{sprite.name}\">";
                    spriteNameCache[key] = result;
                    return result;
                }
            }
        }

        Debug.LogWarning($"[InputIconManager] Sprite not found for key: '{key}'");
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
