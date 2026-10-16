using TMPro;
using UnityEngine;
using UnityEngine.Localization.PropertyVariants;
using UnityEngine.Localization.PropertyVariants.TrackedProperties;
using UnityEngine.Localization.Settings;

public static class LocalizedTextKey
{
    /// <summary>
    /// Points a text localized by a GameObjectLocalizer at another entry of the same string table
    /// Works on inactive objects too: the localizer applies the new entry when they're enabled
    /// </summary>
    public static void Set(GameObject textObject, string key)
    {
        if (textObject == null) return;

        var localizer = textObject.GetComponent<GameObjectLocalizer>();
        var text = textObject.GetComponent<TMP_Text>();
        var property = localizer != null && text != null
            ? localizer.GetTrackedObject(text)?.GetTrackedProperty("m_text") as LocalizedStringProperty
            : null;

        if (property == null)
        {
            Debug.LogWarning($"[LocalizedTextKey] '{textObject.name}' has no localized text to point at '{key}'");
            return;
        }

        property.LocalizedString.SetReference(property.LocalizedString.TableReference, key);

        if (LocalizationSettings.SelectedLocale != null)
            localizer.ApplyLocaleVariant(LocalizationSettings.SelectedLocale);
    }
}
