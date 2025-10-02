using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterData", menuName = "Game/Characters/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("Info")]
    public string characterName;

    [Header("Audio")]
    public AudioClip pelletEatenSound1;
    public AudioClip pelletEatenSound2;
    public AudioClip deathSound;

    [Header("Skins")]
    public CharacterSkin[] skins;

    public CharacterSkin GetSkinByName(string skinName)
    {
        if (skins == null || skins.Length == 0) return null;
        if (string.IsNullOrWhiteSpace(skinName)) return null;

        string target = skinName.Trim();
        return skins.FirstOrDefault(s =>
            s != null &&
            !string.IsNullOrEmpty(s.skinName) &&
            string.Equals(s.skinName.Trim(), target, System.StringComparison.OrdinalIgnoreCase)
        );
    }

    public CharacterSkin DefaultSkinOrFirst()
    {
        if (skins == null || skins.Length == 0) return null;
        return skins.FirstOrDefault(s => s != null);
    }

    public CharacterSkin GetSkinOrDefault(string skinName)
    {
        return GetSkinByName(skinName) ?? DefaultSkinOrFirst();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (skins == null || skins.Length == 0)
        {
            Debug.LogWarning($"[CharacterData] '{name}' no tiene skins asignadas.");
            return;
        }

        var dupGroups = skins
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.skinName))
            .GroupBy(s => s.skinName.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .ToList();

        if (dupGroups.Count > 0)
        {
            string dups = string.Join(", ", dupGroups.Select(g => $"\"{g.Key}\" x{g.Count()}"));
            Debug.LogWarning($"[CharacterData] '{name}' tiene skins con nombres duplicados: {dups}");
        }
    }
#endif
}

[System.Serializable]
public class CharacterSkin
{
    public string skinName;
    public Color arrowIndicatorColor = Color.white;

    [Header("Sprites")]
    public Sprite previewSprite;
    public Sprite lifeIconSprite;
    public Sprite skinIcon;

    [Header("Animator")]
    public RuntimeAnimatorController animatorController;
}