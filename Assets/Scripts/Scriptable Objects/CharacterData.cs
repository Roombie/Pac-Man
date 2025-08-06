using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterData", menuName = "Game/Characters/Character Data")]
public class CharacterData : ScriptableObject
{
    public string characterName;
    public AudioClip pelletEatenSound1;
    public AudioClip pelletEatenSound2;
    public AudioClip deathSound;


    [Header("Skins")]
    public CharacterSkin[] skins;

    public CharacterSkin GetSkinByName(string skinName)
    {
        return skins.FirstOrDefault(s => s.skinName == skinName);
    }
}

[System.Serializable]
public class CharacterSkin
{
    public string skinName;
    public Sprite previewSprite;
    public Sprite lifeIconSprite;
    public Sprite skinIcon;
    public RuntimeAnimatorController animatorController;
}