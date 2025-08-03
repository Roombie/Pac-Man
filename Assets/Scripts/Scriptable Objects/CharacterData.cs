using UnityEngine;

[CreateAssetMenu(fileName = "CharacterData", menuName = "Game/Characters/Character Data")]
public class CharacterData : ScriptableObject
{
    public string characterName;

    [Header("Skins")]
    public CharacterSkin[] skins;
}

[System.Serializable]
public class CharacterSkin
{
    public string skinName;
    public Sprite previewSprite;
    public Sprite lifeIconSprite;
    public RuntimeAnimatorController animatorController;
}