using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;

public class ExtraDisplayTextUpdater : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI bonusText;
    [SerializeField] private string localizedKey = "BonusExtra_Text";
    [SerializeField] private string pointsSpriteTag = "<sprite name=\"pts_pink\">";

    private void Start()
    {
        UpdateBonusText();
        LocalizationSettings.SelectedLocaleChanged += _ => UpdateBonusText();
    }

    private void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= _ => UpdateBonusText();
    }

    private void OnEnable()
    {
        UpdateBonusText();
        LocalizationSettings.SelectedLocaleChanged += _ => UpdateBonusText();
    }

    public void UpdateBonusText()
    {
        string rawValue = PlayerPrefs.GetString(SettingsKeys.ExtraKey, "None");

        if (rawValue == "None")
        {
            bonusText.text = ""; // Hide text if disabled
            return;
        }

        // It should be something like this: "Bonus Pac-Man for {0}"
        string localizedFormat = LocalizationSettings.StringDatabase.GetLocalizedString("GameText", localizedKey);

        // Compose final string with points and sprite
        string final = string.Format(localizedFormat, $"{rawValue}  {pointsSpriteTag}");
        bonusText.text = final;
    }
}
